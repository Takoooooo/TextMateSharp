using System.IO;

using Newtonsoft.Json;

namespace TextMateSharp.Internal.Parser.Json
{
    public class JSONPListParser<T>
    {

        private bool theme;

        public JSONPListParser(bool theme)
        {
            this.theme = theme;
        }

        public T Parse(StreamReader contents)
        {
            PList<T> pList = new PList<T>(theme);

            JsonReader reader = new JsonTextReader(contents);

            while (true)
            {
                if (!reader.Read())
                    break;

                JsonToken nextToken = reader.TokenType;
                switch (nextToken)
                {
                    case JsonToken.StartArray:
                        pList.StartElement("array");
                        break;
                    case JsonToken.EndArray:
                        pList.EndElement("array");
                        break;
                    case JsonToken.StartObject:
                        pList.StartElement("dict");
                        break;
                    case JsonToken.EndObject:
                        pList.EndElement("dict");
                        break;
                    case JsonToken.PropertyName:
                        string lastName = (string)reader.Value;
                        pList.StartElement("key");
                        pList.AddString(lastName);
                        pList.EndElement("key");
                        break;
                    case JsonToken.String:
                        string value = (string)reader.Value;
                        pList.StartElement("string");
                        pList.AddString(value);
                        pList.EndElement("string");
                        break;
                    case JsonToken.Null:
                    case JsonToken.Boolean:
                    case JsonToken.Integer:
                    case JsonToken.Float:
                        break;
                }
            }
            return pList.GetResult();
        }
    }
}