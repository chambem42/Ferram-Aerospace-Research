using System;
using System.Reflection;
using FerramAerospaceResearch.Interfaces;

namespace FerramAerospaceResearch.Reflection
{
    /// <summary>
    /// Base class for config value reflection
    /// </summary>
    public class ValueReflection
    {
        /// <summary>
        /// Copy constructor that will rebind to a provided MemberInfo if not null
        /// </summary>
        /// <param name="other">Value to copy from</param>
        /// <param name="mi">If provided, bind getter and setter to it</param>
        protected ValueReflection(ValueReflection other, MemberInfo mi = null)
        {
            // simple copy is enough
            Name = other.Name;
            ValueType = other.ValueType;
            DeclaringType = mi?.DeclaringType ?? other.DeclaringType;

            // if MemberInfo is provided use it to setup the correct getter and setter, otherwise just copy
            // this is useful for when the same type is reused with different declaring types
            Info = mi == null ? other.Info : SetupInfo(mi);
        }

        protected ValueReflection()
        {
        }

        /// <summary>
        /// Name of the reflected value in the config
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// Type of the config value
        /// </summary>
        public Type ValueType { get; protected set; }

        /// <summary>
        /// Type of declaring type
        /// </summary>
        public Type DeclaringType { get; protected set; }

        /// <summary>
        /// Whether this value is contained in static class
        /// </summary>
        protected bool StaticParser
        {
            get { return DeclaringType == null || DeclaringType.IsStatic(); }
        }

        private GetterSetter Info { get; set; }

        private static GetterSetter SetupInfo(MemberInfo mi)
        {
            switch (mi)
            {
                case FieldInfo fi:
                    if (typeof(IConfigValue).IsBaseOf(fi.FieldType))
                        return new CVFieldMember(fi);
                    return new FieldMember(fi);

                case PropertyInfo fi:
                    if (typeof(IConfigValue).IsBaseOf(fi.PropertyType))
                        return new CVPropertyMember(fi);
                    return new PropertyMember(fi);

                default:
                    throw new
                        ArgumentException($"Invalid argument, expected either FieldInfo or PropertyInfo but got {mi.GetType().Name}",
                                          nameof(mi));
            }
        }

        /// <summary>
        /// Set the value of property/field this reflection points to
        /// </summary>
        /// <param name="instance">Instance of the object to set the value to, null for static classes</param>
        /// <param name="value">New value</param>
        /// <param name="suppressReadOnly">If true will silently fail for readonly members</param>
        public void SetMember(object instance, object value, bool suppressReadOnly = false)
        {
            CheckInstanceType(instance, SourceInfo.Current());
            CheckValueType(value, SourceInfo.Current());
            if (suppressReadOnly && !Info.CanWrite)
                return;
            Info.Set(instance, value);
        }

        /// <summary>
        /// Get the value of property/field this reflection points to
        /// </summary>
        /// <param name="instance">Instance of the object to retrieve the value from</param>
        /// <returns>The retrieved value</returns>
        public object GetMember(object instance)
        {
            CheckInstanceType(instance, SourceInfo.Current());
            return Info.Get(instance);
        }

        /// <summary>
        /// A factory method for value reflections since virtual methods cannot be called in constructors
        /// </summary>
        /// <param name="reflection">the reflection to setup</param>
        /// <param name="mi">member info this reflection will use</param>
        /// <param name="attribute">optional attribute of the field/property, if null will try to get it automatically</param>
        /// <typeparam name="T">child class of ValueReflection</typeparam>
        /// <returns>reflection ready to use</returns>
        protected static T Factory<T>(T reflection, MemberInfo mi, ConfigValueAttribute attribute = null)
            where T : ValueReflection
        {
            // mi may be null when reflected value is not a member of a type
            if (mi is null)
                reflection.Info = reflection.ValueType is null ? null : new SingletonMember(reflection.ValueType);
            else
                reflection.Info = SetupInfo(mi);

            // this involves a virtual call so cannot be used in constructors
            // if mi is null, use existing value type
            reflection.Setup(mi, reflection.Info?.ValueType ?? reflection.ValueType, attribute);

            return reflection;
        }

        public static ValueReflection Create(MemberInfo mi, ConfigValueAttribute attribute = null)
        {
            return Factory(new ValueReflection(), mi, attribute);
        }

        private void Setup(MemberInfo mi, Type type, ConfigValueAttribute attribute = null)
        {
            // try and get the attribute since it will override some of the options
            attribute ??= type?.GetCustomAttribute<ConfigValueAttribute>();

            DeclaringType = mi?.DeclaringType;

            // if name has not been yet setup, use the name from the attribute if it was specified, otherwise default to the member name
            Name ??= attribute?.Name ?? mi?.Name;

            // if value type has not been setup, try to deduce it from the member type, then try to use type from the attribute or fallback to the provided type
            ValueType ??= ReflectionUtils.ConfigValueType(type) ?? attribute?.Type ?? type;

            // delegate the remaining setup to children
            OnSetup(type, attribute);
        }

        /// <summary>
        /// Finalize this reflection setup
        /// </summary>
        /// <param name="type">Type of this reflection</param>
        /// <param name="attribute">ConfigValueAttribute for this reflection, null if not found</param>
        protected virtual void OnSetup(Type type, ConfigValueAttribute attribute)
        {
        }

        protected void CheckInstanceType(object instance, SourceInfo current)
        {
            FARLogger.AssertFormat(StaticParser && instance == null ||
                                   instance != null && (DeclaringType?.IsBaseOf(instance.GetType()) ?? true),
                                   "Invalid instance type {0}, expected {1}",
                                   current,
                                   instance?.GetType(),
                                   DeclaringType);
        }

        protected void CheckValueType(object value, SourceInfo current)
        {
            FARLogger.AssertFormat(ValueType.IsBaseOf(value.GetType()),
                                   "Invalid value type {0}, expected {1}",
                                   current,
                                   value.GetType(),
                                   ValueType);
        }

        // Virtual classes for FieldInfo/PropertyInfo type erasure

        private abstract class GetterSetter
        {
            public abstract Type ValueType { get; }
            public bool CanWrite { get; protected set; } = true;
            public abstract void Set(object instance, object value);
            public abstract object Get(object instance);
        }

        private class FieldMember : GetterSetter
        {
            public readonly FieldInfo Info;

            public FieldMember(FieldInfo info)
            {
                Info = info;
                CanWrite = !info.IsInitOnly;
            }

            public override Type ValueType
            {
                get { return Info.FieldType; }
            }

            public override void Set(object instance, object value)
            {
                Info.SetValue(instance, value, BindingFlags.NonPublic, null, null);
            }

            public override object Get(object instance)
            {
                return Info.GetValue(instance);
            }
        }

        private class PropertyMember : GetterSetter
        {
            public readonly PropertyInfo Info;

            public PropertyMember(PropertyInfo info)
            {
                Info = info;
                CanWrite = info.CanWrite;
            }

            public override Type ValueType
            {
                get { return Info.PropertyType; }
            }

            public override void Set(object instance, object value)
            {
                Info.SetValue(instance, value, BindingFlags.NonPublic, null, null, null);
            }

            public override object Get(object instance)
            {
                return Info.GetValue(instance);
            }
        }

        private class CVFieldMember : GetterSetter
        {
            public readonly FieldInfo Info;

            public CVFieldMember(FieldInfo info)
            {
                Info = info;
            }

            public override Type ValueType
            {
                get { return Info.FieldType; }
            }

            public override void Set(object instance, object value)
            {
                object v = Info.GetValue(instance);
                ((IConfigValue)v).Set(value);
            }

            public override object Get(object instance)
            {
                object v = Info.GetValue(instance);
                return ((IConfigValue)v).Get();
            }
        }

        private class CVPropertyMember : GetterSetter
        {
            public readonly PropertyInfo Info;

            public CVPropertyMember(PropertyInfo info)
            {
                Info = info;
            }

            public override Type ValueType
            {
                get { return Info.PropertyType; }
            }

            public override void Set(object instance, object value)
            {
                object v = Info.GetValue(instance);
                ((IConfigValue)v).Set(value);
            }

            public override object Get(object instance)
            {
                object v = Info.GetValue(instance);
                return ((IConfigValue)v).Get();
            }
        }

        private class SingletonMember : GetterSetter
        {
            public readonly object Singleton;

            public SingletonMember(Type type)
            {
                ValueType = type;
                Singleton = type.IsStatic() ? null : ReflectionUtils.FindInstance(type);
            }

            public override Type ValueType { get; }

            public override void Set(object instance, object value)
            {
                throw new NotImplementedException();
            }

            public override object Get(object instance)
            {
                return Singleton;
            }
        }
    }
}
