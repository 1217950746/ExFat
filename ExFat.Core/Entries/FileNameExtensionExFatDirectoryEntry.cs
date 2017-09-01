﻿namespace ExFat.Core.Entries
{
    using System;
    using System.Diagnostics;
    using Buffers;
    using Buffer = Buffers.Buffer;

    [DebuggerDisplay("File name part {FileName.Value}")]
    public class FileNameExtensionExFatDirectoryEntry : ExFatDirectoryEntry
    {
        public IValueProvider<ExFatGeneralSecondaryFlags> GeneralSecondaryFlags { get; }
        public IValueProvider<string> FileName { get; }

        public FileNameExtensionExFatDirectoryEntry(Buffer buffer) : base(buffer)
        {
            GeneralSecondaryFlags = new EnumValueProvider<ExFatGeneralSecondaryFlags, Byte>(new BufferUInt8(buffer, 1));
            FileName = new BufferWideString(buffer, 2, 15);
        }
    }
}
