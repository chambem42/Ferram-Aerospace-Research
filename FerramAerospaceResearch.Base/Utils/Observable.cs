using System;
using FerramAerospaceResearch.Interfaces;

namespace FerramAerospaceResearch
{
    public class Observable<T> : Interfaces.IObservable<T>, IConfigValue<T>
    {
        private T value;

        public Observable(T v = default, Func<T, T> transform = null)
        {
            Transform = transform;
            value = transform == null ? v : transform(v);
        }

        public Func<T, T> Transform { get; }

        public void Set(object v)
        {
            if (v is T val)
                Set(val);
        }

        public T Get()
        {
            return Value;
        }

        public void Set(T v)
        {
            Value = v;
        }

        object IConfigValue.Get()
        {
            return Get();
        }

        public T Value
        {
            get { return value; }
            set
            {
                value = Transform == null ? value : Transform(value);

                if (FARUtils.ValuesEqual(this.value, value))
                    return;

                this.value = value;
                OnValueChanged?.Invoke(value);
            }
        }

        object IObservable.Value
        {
            get { return Value; }
            set
            {
                if (value is T val)
                    Value = val;
                else
                    FARLogger.Assert(false, $"Value {value.ToString()} is not of type {typeof(T).ToString()}");
            }
        }

        public event Action<T> OnValueChanged;

        public static implicit operator T(Observable<T> o)
        {
            return o.Value;
        }
    }
}
