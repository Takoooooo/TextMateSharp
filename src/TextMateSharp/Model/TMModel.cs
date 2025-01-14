using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using TextMateSharp.Grammars;

namespace TextMateSharp.Model
{
    public class TMModel : ITMModel
    {
        private const int MAX_LEN_TO_TOKENIZE = 10000;
        private IGrammar grammar;

        private List<IModelTokensChangedListener> listeners;

        Tokenizer tokenizer;

        /** The background thread. */
        private TokenizerThread fThread;

        private IModelLines lines;
        private Queue<int> invalidLines = new Queue<int>();

        private object _lock = new object();
        private ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public TMModel(IModelLines lines)
        {
            this.listeners = new List<IModelTokensChangedListener>();
            this.lines = lines;
            ((AbstractLineList)lines).SetModel(this);
        }

        public bool IsStopped
        {
            get { return this.fThread == null || this.fThread.IsStopped; }
        }

        class TokenizerThread
        {
            public volatile bool IsStopped;

            private string name;
            private TMModel model;
            private TMState lastState;

            public TokenizerThread(string name, TMModel model)
            {
                this.name = name;
                this.model = model;
                this.IsStopped = true;
            }

            public void Run()
            {
                IsStopped = false;

                Thread thread = new Thread(new ThreadStart(ThreadWorker));
                thread.Name = name;
                thread.Priority = ThreadPriority.Lowest;
                thread.IsBackground = true;
                thread.Start();
            }

            public void Stop()
            {
                IsStopped = true;
            }

            void ThreadWorker()
            {
                if (IsStopped)
                {
                    return;
                }

                do
                {
                    int toProcess = -1;

                    lock (this.model._lock)
                    {
                        if (model.invalidLines.Count > 0)
                        {
                            toProcess = model.invalidLines.Dequeue();
                        }
                    }

                    if (toProcess == -1)
                    {
                        this.model._resetEvent.Reset();
                        this.model._resetEvent.WaitOne();
                        continue;
                    }

                    if (model.lines.Get(toProcess).IsInvalid)
                    {
                        try
                        {
                            this.RevalidateTokensNow(toProcess, null);
                        }
                        catch (Exception e)
                        {
                            System.Diagnostics.Debug.WriteLine(e.Message);

                            if (toProcess < model.lines.GetNumberOfLines())
                            {
                                model.InvalidateLine(toProcess);
                            }
                        }
                    }
                } while (!IsStopped && model.fThread != null);
            }

            private void RevalidateTokensNow(int startLine, int? toLineIndexOrNull)
            {
                if (model.tokenizer == null)
                    return;

                model.BuildEventWithCallback(eventBuilder =>
                {
                    int toLineIndex = toLineIndexOrNull ?? 0;
                    if (toLineIndexOrNull == null || toLineIndex >= model.lines.GetNumberOfLines())
                    {
                        toLineIndex = model.lines.GetNumberOfLines() - 1;
                    }

                    long tokenizedChars = 0;
                    long currentCharsToTokenize = 0;
                    long MAX_ALLOWED_TIME = 20;
                    long currentEstimatedTimeToTokenize = 0;
                    long elapsedTime;
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    // Tokenize at most 1000 lines. Estimate the tokenization speed per
                    // character and stop when:
                    // - MAX_ALLOWED_TIME is reached
                    // - tokenizing the next line would go above MAX_ALLOWED_TIME

                    int lineIndex = startLine;
                    while (lineIndex <= toLineIndex && lineIndex < model.GetLines().GetNumberOfLines())
                    {
                        elapsedTime = stopwatch.ElapsedMilliseconds;
                        if (elapsedTime > MAX_ALLOWED_TIME)
                        {
                            // Stop if MAX_ALLOWED_TIME is reached
                            model.InvalidateLine(lineIndex);
                            return;
                        }
                        // Compute how many characters will be tokenized for this line
                        try
                        {
                            currentCharsToTokenize = model.lines.GetLineLength(lineIndex);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        if (tokenizedChars > 0)
                        {
                            // If we have enough history, estimate how long tokenizing this line would take
                            currentEstimatedTimeToTokenize = (long)((double)elapsedTime / tokenizedChars) * currentCharsToTokenize;
                            if (elapsedTime + currentEstimatedTimeToTokenize > MAX_ALLOWED_TIME)
                            {
                                // Tokenizing this line will go above MAX_ALLOWED_TIME
                                model.InvalidateLine(lineIndex);
                                return;
                            }
                        }

                        lineIndex = this.UpdateTokensInRange(eventBuilder, lineIndex, lineIndex) + 1;
                        tokenizedChars += currentCharsToTokenize;
                    }
                });
            }

            public int UpdateTokensInRange(ModelTokensChangedEventBuilder eventBuilder, int startIndex,
                int endLineIndex)
            {
                int nextInvalidLineIndex = startIndex;
                int lineIndex = startIndex;
                while (lineIndex <= endLineIndex && lineIndex < model.lines.GetNumberOfLines())
                {
                    int endStateIndex = lineIndex + 1;
                    LineTokens r = null;
                    string text = null;
                    ModelLine modeLine = model.lines.Get(lineIndex);
                    try
                    {
                        text = model.lines.GetLineText(lineIndex);
                        // Tokenize only the first X characters
                        r = model.tokenizer.Tokenize(text, modeLine.GetState(), 0, MAX_LEN_TO_TOKENIZE);
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine(e.Message);
                    }

                    if (r != null && r.Tokens != null && r.Tokens.Count != 0)
                    {
                        // Cannot have a stop offset before the last token
                        r.ActualStopOffset = Math.Max(r.ActualStopOffset, r.Tokens[r.Tokens.Count - 1].StartIndex + 1);
                    }

                    if (r != null && r.ActualStopOffset < text.Length)
                    {
                        // Treat the rest of the line (if above limit) as one default token
                        r.Tokens.Add(new TMToken(r.ActualStopOffset, new List<string>()));
                        // Use as end state the starting state
                        r.EndState = modeLine.GetState();
                    }

                    if (r == null)
                    {
                        r = new LineTokens(new List<TMToken>() { new TMToken(0, new List<string>()) }, text.Length,
                            modeLine.GetState());
                    }

                    modeLine.SetTokens(r.Tokens);
                    eventBuilder.registerChangedTokens(lineIndex + 1);
                    modeLine.IsInvalid = false;

                    if (endStateIndex < model.lines.GetNumberOfLines())
                    {
                        ModelLine endStateLine = model.lines.Get(endStateIndex);
                        if (endStateLine.GetState() != null && r.EndState.Equals(endStateLine.GetState()))
                        {
                            // The end state of this line remains the same
                            nextInvalidLineIndex = lineIndex + 1;
                            while (nextInvalidLineIndex < model.lines.GetNumberOfLines())
                            {
                                bool isLastLine = nextInvalidLineIndex + 1 >= model.lines.GetNumberOfLines();
                                if (model.lines.Get(nextInvalidLineIndex).IsInvalid
                                    || (!isLastLine && model.lines.Get(nextInvalidLineIndex + 1).GetState() == null)
                                    || (isLastLine && this.lastState == null))
                                {
                                    break;
                                }

                                nextInvalidLineIndex++;
                            }

                            lineIndex = nextInvalidLineIndex;
                        }
                        else
                        {
                            endStateLine.SetState(r.EndState);
                            lineIndex++;
                        }
                    }
                    else
                    {
                        this.lastState = r.EndState;
                        lineIndex++;
                    }
                }

                return nextInvalidLineIndex;
            }
        }

        public IGrammar GetGrammar()
        {
            return grammar;
        }

        public void SetGrammar(IGrammar grammar)
        {
            if (!Object.Equals(grammar, this.grammar))
            {
                this.grammar = grammar;
                this.tokenizer = new Tokenizer(grammar);
                lines.ForEach((line) => line.ResetTokenizationState());
                lines.Get(0).SetState(tokenizer.GetInitialState());
                InvalidateLine(0);
            }
        }

        public void AddModelTokensChangedListener(IModelTokensChangedListener listener)
        {
            if (this.fThread == null || this.fThread.IsStopped)
            {
                this.fThread = new TokenizerThread("TMModelThread", this);
            }

            if (this.fThread.IsStopped)
            {
                this.fThread.Run();
            }

            if (!listeners.Contains(listener))
            {
                listeners.Add(listener);
            }
        }

        public void RemoveModelTokensChangedListener(IModelTokensChangedListener listener)
        {
            listeners.Remove(listener);
            if (listeners.Count == 0)
            {
                // no need to keep tokenizing if no-one cares
                Stop();
            }
        }

        public void Dispose()
        {
            Stop();
            GetLines().Dispose();
        }

        private void Stop()
        {
            if (fThread == null)
            {
                return;
            }

            this.fThread.Stop();
            _resetEvent.Set();
            this.fThread = null;
        }

        private void BuildEventWithCallback(Action<ModelTokensChangedEventBuilder> callback)
        {
            if (this.fThread == null || this.fThread.IsStopped)
                return;

            ModelTokensChangedEventBuilder eventBuilder = new ModelTokensChangedEventBuilder(this);

            callback(eventBuilder);

            ModelTokensChangedEvent e = eventBuilder.Build();
            if (e != null)
            {
                this.Emit(e);
            }
        }

        private void Emit(ModelTokensChangedEvent e)
        {
            foreach (IModelTokensChangedListener listener in listeners)
            {
                listener.ModelTokensChanged(e);
            }
        }

        public void ForceTokenization(int lineIndex)
        {
            if (grammar == null)
                return;

            this.BuildEventWithCallback(eventBuilder =>
                this.fThread.UpdateTokensInRange(eventBuilder, lineIndex, lineIndex)
            );
        }

        public void ForceTokenization(int startLineIndex, int endLineIndex)
        {
            if (grammar == null)
                return;

            this.BuildEventWithCallback(eventBuilder =>
                this.fThread.UpdateTokensInRange(eventBuilder, startLineIndex, endLineIndex)
            );
        }

        public List<TMToken> GetLineTokens(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex > lines.GetNumberOfLines() - 1)
                return null;

            return lines.Get(lineIndex).Tokens;
        }

        public bool IsLineInvalid(int lineIndex)
        {
            return lines.Get(lineIndex).IsInvalid;
        }

        public void InvalidateLine(int lineIndex)
        {
            this.lines.Get(lineIndex).IsInvalid = true;

            lock (_lock)
            {
                this.invalidLines.Enqueue(lineIndex);
                _resetEvent.Set();
            }
        }

        public IModelLines GetLines()
        {
            return this.lines;
        }
    }
}