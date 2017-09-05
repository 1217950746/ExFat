﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.Buffers
{
    using System;
    using System.Diagnostics;

    [DebuggerDisplay("{" + nameof(Value) + "}")]
    public class BufferUInt32 : IValueProvider<UInt32>
    {
        private readonly Buffer _buffer;

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>
        /// The value.
        /// </value>
        public UInt32 Value
        {
            get { return LittleEndian.ToUInt32(_buffer); }
            set { LittleEndian.GetBytes(value, _buffer); }
        }

        public BufferUInt32(Buffer buffer, int offset)
        {
            _buffer = new Buffer(buffer, offset, sizeof(UInt32));
        }
    }
}