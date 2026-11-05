using System;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.Reflection;
using Xunit;

namespace FerramAerospaceResearch.UnitTests
{
    /// <summary>
    /// FAR's reflection helpers: how ReflectionUtils resolves the singleton a config node loads into (a cached instance,
    /// one cached under a derived type, a static Instance field or method), and how NodeReflection with ConfigAdapter's
    /// visitors loads and saves a config type with the members FAR's own config does not use yet: an Observable field, a
    /// fixed-size array, a named nested node, a node list and a member whose setter throws.
    /// </summary>
    [Collection(TestLog.Collection)]
    public class ReflectionTests
    {
        // one set of types per test: the instance cache is static and lives as long as the test process

        private class FieldSingleton
        {
            public static readonly FieldSingleton Instance = new FieldSingleton();
        }

        private class MethodSingleton
        {
            private static readonly MethodSingleton it = new MethodSingleton();
            public static MethodSingleton Instance() => it;
        }

        private class WrongField
        {
            public static object Instance = "not a WrongField";
        }

        private class WrongMethod
        {
            public static string Instance() => "not a WrongMethod";
        }

        private class NoInstance
        {
        }

        private static class StaticType
        {
        }

        private class Base
        {
        }

        private class Derived : Base
        {
        }

        private class OtherBase
        {
        }

        private class OtherDerived : OtherBase
        {
        }

        private class Twice
        {
        }

        [SkippableFact]
        public void AStaticInstanceFieldOrMethodIsTheSingleton()
        {
            UnityRuntime.RequireLog();
            Assert.Same(FieldSingleton.Instance, ReflectionUtils.FindInstance(typeof(FieldSingleton)));
            Assert.Same(MethodSingleton.Instance(), ReflectionUtils.FindInstance(typeof(MethodSingleton)));
            // and cached: found again without the lookup
            Assert.Same(FieldSingleton.Instance, ReflectionUtils.FindInstance(typeof(FieldSingleton)));
        }

        [SkippableFact]
        public void AnInstanceOfTheWrongTypeIsNotTheSingleton()
        {
            UnityRuntime.RequireLog();
            Assert.Null(ReflectionUtils.FindInstance(typeof(WrongField)));
            Assert.Null(ReflectionUtils.FindInstance(typeof(WrongMethod)));
            Assert.Null(ReflectionUtils.FindInstance(typeof(NoInstance)));
            Assert.Null(ReflectionUtils.FindInstance(typeof(StaticType)));
        }

        [SkippableFact]
        public void AnInstanceRegisteredAsADerivedTypeIsFoundFromItsBase()
        {
            UnityRuntime.RequireLog();
            var derived = new Derived();
            Assert.Same(derived, ReflectionUtils.RegisterInstance(typeof(Derived), derived));
            Assert.Same(derived, ReflectionUtils.FindInstance(typeof(Base)));

            // registered under its base type, it is cached under its own type too
            var other = new OtherDerived();
            ReflectionUtils.RegisterInstance(typeof(OtherBase), other);
            Assert.Same(other, ReflectionUtils.FindInstance(typeof(OtherDerived)));
        }

        [SkippableFact]
        public void ASecondInstanceOfATypeIsRefusedAndLogged()
        {
            UnityRuntime.RequireLog();
            var first = new Twice();
            ReflectionUtils.RegisterInstance(typeof(Twice), first);
            ReflectionUtils.RegisterInstance(typeof(Twice), new Twice());
            Assert.Same(first, ReflectionUtils.FindInstance(typeof(Twice)));
            Assert.Contains(TestLog.Instance.Lines,
                            l => l.StartsWith("Error") && l.Contains("Could not register instance") && l.Contains(nameof(Twice)));
        }

        [Theory]
        [InlineData(typeof(IList<int>), typeof(int))]
        [InlineData(typeof(List<string>), typeof(string))]
        [InlineData(typeof(float[]), typeof(float))]
        [InlineData(typeof(int), null)]
        [InlineData(typeof(IEnumerable<int>), null)]
        public void ListTypeIsTheListItemType(Type type, Type item)
        {
            Assert.Equal(item, ReflectionUtils.ListType(type));
        }

        // a config type with the member kinds FAR's own config does not have

        [ConfigNode("INNER")]
        public class Inner
        {
            [ConfigValue("text")] public string Text { get; set; } = "";

            [ConfigValue("bad")]
            public int Bad
            {
                get => 0;
                set
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }

        [ConfigNode("ITEM")]
        public class Item
        {
            [ConfigValue("value")] public int Value { get; set; }

            [ConfigValue("bad")]
            public int Bad
            {
                get => 0;
                set
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }

        [ConfigNode("TestConfig")]
        public class TestConfig
        {
            [ConfigValue("period")] public Observable<int> Period = new Observable<int>(5);
            [ConfigValue("weights")] public int[] Weights = new int[3];
            [ConfigValue("inner")] public Inner Inner = new Inner();
            [ConfigValue] public List<Item> Items = new List<Item>();

            [ConfigValue("bad")]
            public int Bad
            {
                get => 0;
                set
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }

        private static NodeReflection Reflection => NodeReflection.GetReflection(typeof(TestConfig));

        private static int Load(ConfigNode node, TestConfig config)
        {
            object instance = config;
            return Reflection.Load(new LoadVisitor { Node = node }, ref instance);
        }

        private static ConfigNode Save(TestConfig config, bool patch = false)
        {
            var node = new ConfigNode("TestConfig");
            Assert.Equal(0, Reflection.Save(new SaveVisitor { Node = node, IsPatch = patch }, config));
            return node;
        }

        private static ConfigNode TestNode()
        {
            var node = new ConfigNode("TestConfig");
            node.AddValue("period", 12);
            foreach (int w in new[] { 1, 2, 3, 4 })
                node.AddValue("weights", w);
            ConfigNode inner = node.AddNode("INNER");
            inner.AddValue("name", "inner");
            inner.AddValue("text", "hello");
            node.AddNode("ITEM").AddValue("value", 1);
            node.AddNode("ITEM").AddValue("value", 2);
            return node;
        }

        [SkippableFact]
        public void EveryKindOfMemberLoads()
        {
            UnityRuntime.RequireLog();
            var config = new TestConfig();
            int[] weights = config.Weights;
            int seen = 0;
            config.Period.OnValueChanged += v => seen = v;

            Assert.Equal(0, Load(TestNode(), config));
            Assert.Equal(12, config.Period.Value);
            Assert.Equal(12, seen);
            Assert.Same(weights, config.Weights);              // filled in place, the extra value dropped
            Assert.Equal(new[] { 1, 2, 3 }, config.Weights);
            Assert.Equal("hello", config.Inner.Text);
            Assert.Equal(new[] { 1, 2 }, config.Items.Select(i => i.Value));
        }

        [SkippableFact]
        public void AThrowingMemberIsCountedAndTheRestLoads()
        {
            UnityRuntime.RequireLog();
            ConfigNode node = TestNode();
            node.AddValue("bad", -1);
            node.GetNode("INNER").AddValue("bad", -1);
            node.GetNodes("ITEM")[1].AddValue("bad", -1);
            var config = new TestConfig();
            TestLog.Instance.Clear();

            Assert.Equal(1, Load(node, config));   // the nested node's and the item's errors are logged there
            Assert.Equal(12, config.Period.Value);
            Assert.Equal("hello", config.Inner.Text);
            Assert.Equal(new[] { 1, 2 }, config.Items.Select(i => i.Value));
            Assert.Contains(TestLog.Instance.Lines, l => l.StartsWith("Exception") && l.Contains("Exception loading value bad"));
            Assert.Contains(TestLog.Instance.Lines, l => l.StartsWith("Error") && l.Contains("1 errors while loading INNER[inner]"));
            Assert.Contains(TestLog.Instance.Lines, l => l.StartsWith("Error") && l.Contains("1 errors while loading ITEM[1]"));
        }

        [SkippableFact]
        public void ASavedConfigLoadsBackToTheSameValues()
        {
            UnityRuntime.RequireLog();
            var config = new TestConfig();
            Assert.Equal(0, Load(TestNode(), config));
            ConfigNode saved = Save(config);
            Assert.Equal(new[] { "1", "2", "3" }, saved.GetValues("weights"));
            Assert.Equal("inner", saved.GetNode("INNER").GetValue("name"));
            Assert.Equal(2, saved.GetNodes("ITEM").Length);

            var back = new TestConfig();
            Assert.Equal(0, Load(saved, back));
            Assert.Equal(12, back.Period.Value);
            Assert.Equal(new[] { 1, 2, 3 }, back.Weights);
            Assert.Equal("hello", back.Inner.Text);
            Assert.Equal(new[] { 1, 2 }, back.Items.Select(i => i.Value));
            Assert.Equal(saved.ToString(), Save(back).ToString());
        }

        [SkippableFact]
        public void APatchSaveEditsValuesAndReplacesLists()
        {
            UnityRuntime.RequireLog();
            var config = new TestConfig();
            Assert.Equal(0, Load(TestNode(), config));
            ConfigNode patch = Save(config, true);

            Assert.Equal("12", patch.GetValue("%period"));
            // the list is deleted and written again, so the patched config gets exactly these items
            Assert.Equal("deleted", patch.GetValue("!weights"));
            Assert.Equal(new[] { "1", "2", "3" }, patch.GetValues("weights"));
            string[] names = patch.values.Cast<ConfigNode.Value>().Select(v => v.name).ToArray();
            Assert.True(Array.IndexOf(names, "!weights") < Array.IndexOf(names, "weights"), string.Join(",", names));

            Assert.Equal("hello", patch.GetNode("@INNER[inner]")?.GetValue("%text"));
            Assert.True(patch.HasNode("!ITEM,*"), patch.ToString());
            Assert.Equal(new[] { "1", "2" }, patch.GetNodes("ITEM").Select(n => n.GetValue("value")));
        }
    }
}
