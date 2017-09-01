﻿namespace ExFat.Core.Buffers
{
    using System.Diagnostics;

    [DebuggerDisplay("{" + nameof(Value) + "}")]
    public class EnumValueProvider<TEnum, TBacking> : IValueProvider<TEnum>
    {
        private readonly IValueProvider<TBacking> _backingValueProvider;

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>
        /// The value.
        /// </value>
        public TEnum Value
        {
            // the casts are a bit dirty here, however they do the job
            get { return (TEnum)(object)_backingValueProvider.Value; }
            set { _backingValueProvider.Value = (TBacking)(object)value; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EnumValueProvider{TEnum, TBacking}"/> class.
        /// </summary>
        /// <param name="backingValueProvider">The backing value provider.</param>
        public EnumValueProvider(IValueProvider<TBacking> backingValueProvider)
        {
            _backingValueProvider = backingValueProvider;
        }
    }
}