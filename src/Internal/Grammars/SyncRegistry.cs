using System;
using System.Collections.Generic;

using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Parser;
using TextMateSharp.Internal.Types;

using TextMateSharp.Themes;

namespace TextMateSharp.Internal.Grammars
{
    public class SyncRegistry : IGrammarRepository, IThemeProvider
    {
        private Dictionary<string, IGrammar> grammars;
        private Dictionary<string, IRawGrammar> rawGrammars;
        private Dictionary<string, ICollection<string>> injectionGrammars;
        private Theme theme;

        public SyncRegistry(Theme theme)
        {
            this.theme = theme;
            this.grammars = new Dictionary<string, IGrammar>();
            this.rawGrammars = new Dictionary<string, IRawGrammar>();
            this.injectionGrammars = new Dictionary<string, ICollection<string>>();
        }

        public void SetTheme(Theme theme)
        {
            this.theme = theme;

            foreach (IGrammar grammar in grammars.Values)
                ((Grammar)grammar).OnDidChangeTheme();

        }

        public IEnumerable<string> GetColorMap()
        {
            return this.theme.GetColorMap();
        }

        public ICollection<string> AddGrammar(IRawGrammar grammar, ICollection<string> injectionScopeNames)
        {
            this.rawGrammars.Add(grammar.GetScopeName(), grammar);
            ICollection<string> includedScopes = new List<string>();
            CollectIncludedScopes(includedScopes, grammar);

            if (injectionScopeNames != null)
            {
                this.injectionGrammars.Add(grammar.GetScopeName(), injectionScopeNames);
                foreach (string scopeName in injectionScopeNames)
                {
                    AddIncludedScope(scopeName, includedScopes);
                }
            }
            return includedScopes;
        }

        public IRawGrammar Lookup(string scopeName)
        {
            IRawGrammar result;
            this.rawGrammars.TryGetValue(scopeName, out result);
            return result;
        }

        public ICollection<string> Injections(string targetScope)
        {
            ICollection<string> result;
            this.injectionGrammars.TryGetValue(targetScope, out result);
            return result;
        }

        public ThemeTrieElementRule GetDefaults()
        {
            return this.theme.getDefaults();
        }

        public List<ThemeTrieElementRule> ThemeMatch(String scopeName)
        {
            return this.theme.match(scopeName);
        }

        /**
		 * Lookup a grammar.
		 */
        public IGrammar GrammarForScopeName(String scopeName, int initialLanguage,
                Dictionary<string, int> embeddedLanguages)
        {
            if (!this.grammars.ContainsKey(scopeName))
            {
                IRawGrammar rawGrammar = Lookup(scopeName);
                if (rawGrammar == null)
                {
                    return null;
                }
                this.grammars.Add(scopeName,
                        GrammarHelper.CreateGrammar(rawGrammar, initialLanguage, embeddedLanguages, this, this));
            }
            return this.grammars[scopeName];
        }

        private static void CollectIncludedScopes(ICollection<String> result, IRawGrammar grammar)
        {
            if (grammar.GetPatterns() != null /* && Array.isArray(grammar.patterns) */)
            {
                ExtractIncludedScopesInPatterns(result, grammar.GetPatterns());
            }

            IRawRepository repository = grammar.GetRepository();
            if (repository != null)
            {
                ExtractIncludedScopesInRepository(result, repository);
            }

            // remove references to own scope (avoid recursion)
            result.Remove(grammar.GetScopeName());
        }

        private static void ExtractIncludedScopesInPatterns(ICollection<String> result, ICollection<IRawRule> patterns)
        {
            foreach (IRawRule pattern in patterns)
            {
                ICollection<IRawRule> p = pattern.GetPatterns();
                if (p != null)
                {
                    ExtractIncludedScopesInPatterns(result, p);
                }

                String include = pattern.GetInclude();
                if (include == null)
                {
                    continue;
                }

                if (include.Equals("$base") || include.Equals("$self"))
                {
                    // Special includes that can be resolved locally in this grammar
                    continue;
                }

                if (include[0] == '#')
                {
                    // Local include from this grammar
                    continue;
                }

                int sharpIndex = include.IndexOf('#');
                if (sharpIndex >= 0)
                {
                    AddIncludedScope(include.Substring(0, sharpIndex), result);
                }
                else
                {
                    AddIncludedScope(include, result);
                }
            }
        }

        private static void AddIncludedScope(string scopeName, ICollection<String> includedScopes)
        {
            if (!includedScopes.Contains(scopeName))
            {
                includedScopes.Add(scopeName);
            }
        }

        private static void ExtractIncludedScopesInRepository(ICollection<string> result, IRawRepository repository)
        {
            if (!(repository is Raw))
            {
                return;
            }
            Raw rawRepository = (Raw)repository;
            foreach (string key in rawRepository.Keys)
            {
                IRawRule rule = (IRawRule)rawRepository[key];
                if (rule.GetPatterns() != null)
                {
                    ExtractIncludedScopesInPatterns(result, rule.GetPatterns());
                }
                if (rule.GetRepository() != null)
                {
                    ExtractIncludedScopesInRepository(result, rule.GetRepository());
                }
            }
        }
    }
}