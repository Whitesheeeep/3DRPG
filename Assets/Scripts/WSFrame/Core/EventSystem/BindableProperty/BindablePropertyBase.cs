using System;

namespace WS_Modules.CustomEventSystem
{
    public interface IReadOnlyBindableProperty<T> : IRegister
    {
        T Value { get; }

        IUnRegister RegisterWithInitValue(Action<T> action);

        void UnRegister(Action<T> onValueOnChanged);

        IUnRegister Register(Action<T> action);
    }

    public interface IBindableProperty<T> : IReadOnlyBindableProperty<T>
    {
        new T Value { get; set; }

        void SetValueWithoutNotify(T value);
    }

    public class BindableProperty<T> : IBindableProperty<T>
    {
        public BindableProperty(T defaultValue = default)
        {
            _value = defaultValue;
        }

        protected T _value;

        private EasyEvent<T> _onValueChanged = new();

        public static Func<T, T, bool> Comparer { get; set; } = (a, b) => a.Equals(b);

        public BindableProperty<T> WithComparer(Func<T, T, bool> comparer)
        {
            Comparer = comparer;
            return this;
        }

        #region T 属性的获取与设置
        public T Value
        {
            get => GetValue();
            set
            {
                if (value == null && _value == null) return;
                if (value != null && Comparer(value, _value)) return;

                SetValue(value);
                _onValueChanged.Invoke(_value);
            }
        }

        // 子类可以自定义 SetValue 和 GetValue 的行为，例如在 SetValue 中添加额外的逻辑（如触发事件），或者在 GetValue 中添加计算逻辑。
        protected virtual void SetValue(T newValue) => _value = newValue;

        protected virtual T GetValue() => _value;
        #endregion

        public void SetValueWithoutNotify(T value) => _value = value;

        public void UnRegister(Action<T> onValueOnChanged) => _onValueChanged.Unregister(onValueOnChanged);

        public IUnRegister Register(Action<T> onValueChanged) => _onValueChanged.Register(onValueChanged);

        /// <summary>
        /// 注册一个带有初始值的事件处理程序，这样在注册时就能立即获取当前的值，而不需要等待值发生变化时才触发事件。
        /// 这对于需要在注册时立即获取当前状态的场景非常有用，例如 UI 组件需要在绑定属性时立即显示当前值。
        /// 但是要保证有效的数据要在注册之前设置好，否则可能会导致注册时获取到的值不正确或者是默认值。
        /// </summary>
        /// <param name="onValueOnChanged"></param>
        /// <returns></returns>
        public IUnRegister RegisterWithInitValue(Action<T> onValueOnChanged)
        {
            onValueOnChanged?.Invoke(_value);
            return Register(onValueOnChanged);
        }

        IUnRegister IRegister.Register(Action onAnyEventInvoke)
        {
            return Register(Action);
            void Action(T _) => onAnyEventInvoke();
        }

        public override string ToString() => Value.ToString();
    }

    /// <summary>
    /// 自动注册 BindableProperty 的 Comparer
    /// </summary>
    internal class ComparerAutoRegister
    {
#if UNITY_5_6_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void AutoRegister()
        {
            BindableProperty<int>.Comparer = (a, b) => a == b;
            BindableProperty<float>.Comparer = (a, b) => a.Equals(b);
            BindableProperty<double>.Comparer = (a, b) => a.Equals(b);
            BindableProperty<string>.Comparer = (a, b) => a == b;
            BindableProperty<long>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector2>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector3>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector4>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Color>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Color32>.Comparer =
                (a, b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
            BindableProperty<UnityEngine.Bounds>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Rect>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Quaternion>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector2Int>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.Vector3Int>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.BoundsInt>.Comparer = (a, b) => a == b;
            BindableProperty<UnityEngine.RangeInt>.Comparer = (a, b) => a.start == b.start && a.length == b.length;
            BindableProperty<UnityEngine.RectInt>.Comparer = (a, b) => a.Equals(b);
        }
#endif
    }

    public static class BindablePropertyExtensions
    {
        public static OrEvent Or(this IRegister register, IRegister orRegister)
        {
            return new OrEvent().Or(register).Or(orRegister);
        }
    }
}
