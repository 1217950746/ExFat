﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.Partition
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Entries;
    using IO;
    using Buffer = Buffers.Buffer;

    public class ExFatDirectory : IDisposable
    {
        private readonly PartitionStream _directoryStream;
        private readonly bool _ownsStream;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExFatDirectory"/> class.
        /// </summary>
        /// <param name="directoryStream">The directory stream.</param>
        /// <param name="ownsStream">if set to <c>true</c> [owns stream].</param>
        public ExFatDirectory(PartitionStream directoryStream, bool ownsStream)
        {
            _directoryStream = directoryStream;
            _ownsStream = ownsStream;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            if (_ownsStream)
                _directoryStream.Dispose();
        }

        /// <summary>
        /// Gets the entries, totally raw (includes the deleted entries).
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ExFatDirectoryEntry> GetEntries()
        {
            if (_directoryStream.CanSeek)
                _directoryStream.Seek(0, SeekOrigin.Begin);
            for (var offset = 0L;; offset += 32)
            {
                var entryBytes = new byte[32];
                // cluster offset before reading data, since it's the start
                var clusterPosition = _directoryStream.ClusterPosition;
                if (_directoryStream.Read(entryBytes, 0, entryBytes.Length) != 32)
                    break;
                var directoryEntry = ExFatDirectoryEntry.Create(new Buffer(entryBytes), offset, clusterPosition);
                if (directoryEntry != null)
                    yield return directoryEntry;
            }
        }

        /// <summary>
        /// Gets the entries grouped: one primary followed by its secondaries.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ExFatMetaDirectoryEntry> GetMetaEntries()
        {
            var entriesStack = new List<ExFatDirectoryEntry>();
            foreach (var directoryEntry in GetEntries())
            {
                if (!directoryEntry.InUse)
                    continue;

                if (directoryEntry.IsSecondary)
                    entriesStack.Add(directoryEntry);
                else
                {
                    if (entriesStack.Count > 0)
                        yield return new ExFatMetaDirectoryEntry(entriesStack);
                    entriesStack.Clear();
                    entriesStack.Add(directoryEntry);
                }
            }
            if (entriesStack.Count > 0)
                yield return new ExFatMetaDirectoryEntry(entriesStack);
        }

        public void UpdateEntry(ExFatDirectoryEntry entry)
        {
            _directoryStream.Seek(entry.Position, SeekOrigin.Begin);
            entry.Write(_directoryStream);
        }

        public void UpdateEntry(ExFatMetaDirectoryEntry entry)
        {
            _directoryStream.Seek(entry.Primary.Position, SeekOrigin.Begin);
            entry.Write(_directoryStream);
        }
    }
}