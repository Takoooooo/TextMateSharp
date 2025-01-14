﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;

using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Registry;
using TextMateSharp.Tests.Resources;
using TextMateSharp.Themes;

namespace TextMateSharp.Tests.Internal.Themes
{
    class ThemeTest
    {
        [Test]
        public void EnsureMoreSpecificRulesAreFirst()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[] { "keyword.control" });

            Assert.AreEqual(
                "#C586C0",
                theme.GetColor(rules[0].foreground));
        }

        [Test]
        public void EnsureRulesDefinedFirstAreMoreSpecific()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "keyword.control.directive.include.c",
                "meta.preprocessor.include.c"
            });

            Assert.AreEqual(
                "#C586C0",
                theme.GetColor(rules[0].foreground));
        }
        
        [Test]
        public void PHPVariableTest()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "string.quoted.double.php",
                "variable.other.php"
            });

            Assert.AreEqual(
                "#9CDCFE",
                theme.GetColor(rules[0].foreground));
        }

        [Test]
        public void EnsureMainThemeRulesAreMoreSpecificTest()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "meta.embedded.block.html",
                "comment.line.double-slash.js"
            });

            Assert.AreEqual(
                "#6A9955",
                theme.GetColor(rules[0].foreground));
        }

        [Test]
        public void EnsureColoredRulesAreReturnedFirstTest()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "text.html.basic" ,
                "meta.embedded.block.html",
                "source.js",
                "comment.line.double-slash.js",
                "punctuation.definition.comment.js"
            });

            Assert.AreEqual(
                "#6A9955",
                theme.GetColor(rules[0].foreground));
        }

        [Test]
        public void JsonKeyColorTest()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "source.json",
                "meta.structure.dictionary.json",
                "string.json",
                "support.type.property-name.json"
            });

            string color = theme.GetColor(rules[0].foreground);

            Assert.AreEqual(
                "#9CDCFE",
                color);
        }

        [Test]
        public void EnsureScriptTagIsColored()
        {
            IRegistryOptions registryOptions = new TestRegistry();

            Theme theme = Theme.CreateFromRawTheme(
                registryOptions.GetTheme(),
                registryOptions);

            var rules = theme.Match(new string[]
            {
                "text.html.basic",
                "meta.embedded.block.html",
                "meta.tag.metadata.script.start.html",
                "entity.name.tag.html"
            });

            Assert.AreEqual(
                "#569CD6",
                theme.GetColor(rules[0].foreground));
        }

        class TestRegistry : IRegistryOptions
        {
            Stream IRegistryOptions.GetInputStream(string scopeName)
            {
                if (scopeName == "./dark_vs.json")
                    scopeName = "dark_vs.json";

                return ResourceReader.OpenStream(scopeName);
            }

            ICollection<string> IRegistryOptions.GetInjections(string scopeName)
            {
                return null;
            }

            string IRegistryOptions.GetFilePath(string scopeName)
            {
                return string.Empty;
            }

            IRawTheme IRegistryOptions.GetTheme()
            {
                using (Stream stream = ResourceReader.OpenStream("dark_plus.json"))
                using (StreamReader reader = new StreamReader(stream))
                {
                    return ThemeReader.ReadThemeSync(reader);
                }
            }
        }
    }
}
