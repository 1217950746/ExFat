﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.Filesystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using IO;
    using Partition;
    using Partition.Entries;
    using Buffer = Buffers.Buffer;

    /// <summary>
    /// Filesystem access at low-level: entry manipulation
    /// (high level is <see cref="ExFatPathFilesystem"/> which works with paths)
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public class ExFatEntryFilesystem : IDisposable
    {
        private readonly ExFatFilesystemFlags _flags;
        private readonly ExFatPartition _partition;
        private readonly object _lock = new object();

        private ExFatFilesystemEntry _rootDirectory;

        /// <summary>
        /// Gets the root directory.
        /// </summary>
        /// <value>
        /// The root directory.
        /// </value>
        public ExFatFilesystemEntry RootDirectory
        {
            get
            {
                if (_rootDirectory == null)
                    _rootDirectory = CreateRootDirectory();
                return _rootDirectory;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Initializes a new instance of the <see cref="T:ExFat.Filesystem.ExFatEntryFilesystem" /> class.
        /// </summary>
        /// <param name="partitionStream">The partition stream.</param>
        /// <param name="flags">The flags.</param>
        public ExFatEntryFilesystem(Stream partitionStream, ExFatFilesystemFlags flags = ExFatFilesystemFlags.Default)
            : this(new ExFatPartition(partitionStream), flags)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExFatEntryFilesystem"/> class.
        /// </summary>
        /// <param name="partition">The partition.</param>
        /// <param name="flags">The flags.</param>
        public ExFatEntryFilesystem(ExFatPartition partition, ExFatFilesystemFlags flags = ExFatFilesystemFlags.Default)
        {
            _flags = flags;
            _partition = partition;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            _partition.Dispose();
        }

        private ExFatFilesystemEntry CreateRootDirectory()
        {
            return new ExFatFilesystemEntry(_partition.RootDirectoryDataDescriptor, dataDescriptorOverride: _partition.RootDirectoryDataDescriptor,
                attributesOverride: ExFatFileAttributes.Directory);
        }

        /// <summary>
        /// Enumerates the file system entries.
        /// </summary>
        /// <param name="directoryEntry">The directory entry.</param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        public IEnumerable<ExFatFilesystemEntry> EnumerateFileSystemEntries(ExFatFilesystemEntry directoryEntry)
        {
            if (!directoryEntry.IsDirectory)
                throw new InvalidOperationException();

            foreach (var metaEntry in _partition.GetMetaEntries(directoryEntry.DataDescriptor))
            {
                // keep only file entries
                if (metaEntry.Primary is FileExFatDirectoryEntry)
                    yield return new ExFatFilesystemEntry(directoryEntry.DataDescriptor, metaEntry);
            }
        }

        /// <summary>
        /// Finds a child with given name.
        /// </summary>
        /// <param name="directoryEntry">The directory entry.</param>
        /// <param name="name">The name.</param>
        /// <returns></returns>
        /// <exception cref="System.InvalidOperationException"></exception>
        public ExFatFilesystemEntry FindChild(ExFatFilesystemEntry directoryEntry, string name)
        {
            if (!directoryEntry.IsDirectory)
                throw new InvalidOperationException();

            var nameHash = _partition.ComputeNameHash(name);
            foreach (var metaEntry in _partition.GetMetaEntries(directoryEntry.DataDescriptor))
            {
                var streamExtension = metaEntry.SecondaryStreamExtension;
                // keep only file entries
                if (streamExtension != null && streamExtension.NameHash.Value == nameHash && metaEntry.ExtensionsFileName == name)
                    return new ExFatFilesystemEntry(directoryEntry.DataDescriptor, metaEntry);
            }
            return null;
        }

        /// <summary>
        /// Opens the specified entry.
        /// </summary>
        /// <param name="fileEntry">The entry.</param>
        /// <param name="access">The access.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Stream OpenFile(ExFatFilesystemEntry fileEntry, FileAccess access)
        {
            if (fileEntry.IsDirectory)
                throw new InvalidOperationException();

            return OpenData(fileEntry, access);
        }

        /// <summary>
        /// Creates the file.
        /// </summary>
        /// <param name="parentDirectory">The parent directory.</param>
        /// <param name="fileName">Name of the file.</param>
        /// <returns></returns>
        public Stream CreateFile(ExFatFilesystemEntry parentDirectory, string fileName)
        {
            if (!parentDirectory.IsDirectory)
                throw new InvalidOperationException();

            var existingFile = FindChild(parentDirectory, fileName);
            if (existingFile != null)
            {
                var stream = OpenData(existingFile, FileAccess.ReadWrite);
                stream.SetLength(0);
                return stream;
            }

            var fileEntry = CreateEntry(parentDirectory, fileName, FileAttributes.Archive);
            var updatedDataDescriptor = _partition.AddEntry(parentDirectory.DataDescriptor, fileEntry.MetaEntry);
            UpdateEntry(parentDirectory, FileAccess.Write, updatedDataDescriptor);
            return OpenData(fileEntry, FileAccess.ReadWrite);
        }

        private PartitionStream OpenData(ExFatFilesystemEntry fileEntry, FileAccess access)
        {
            return _partition.OpenDataStream(fileEntry.DataDescriptor, access, d => UpdateEntry(fileEntry, access, d));
        }

        private void UpdateEntry(ExFatFilesystemEntry entry, FileAccess descriptor, DataDescriptor dataDescriptor)
        {
            if (entry?.MetaEntry == null)
                return;

            DateTimeOffset? now = null;
            var file = (FileExFatDirectoryEntry)entry.MetaEntry.Primary;

            // if file was open for reading and the flag is set, the entry is updated
            if (descriptor.HasAny(FileAccess.Read) && _flags.HasAny(ExFatFilesystemFlags.UpdateLastAccessTime))
            {
                now = DateTimeOffset.Now;
                file.LastAccessDateTimeOffset.Value = now.Value;
            }

            // when it was open for writing, its characteristics may have changed, so we update them
            if (descriptor.HasAny(FileAccess.Write))
            {
                now = now ?? DateTimeOffset.Now;
                file.FileAttributes.Value |= ExFatFileAttributes.Archive;
                file.LastWriteDateTimeOffset.Value = now.Value;
                var stream = entry.MetaEntry.SecondaryStreamExtension;
                if (dataDescriptor.Contiguous)
                    stream.GeneralSecondaryFlags.Value |= ExFatGeneralSecondaryFlags.NoFatChain;
                else
                    stream.GeneralSecondaryFlags.Value &= ~ExFatGeneralSecondaryFlags.NoFatChain;
                stream.FirstCluster.Value = (UInt32)dataDescriptor.FirstCluster.Value;
                stream.ValidDataLength.Value = dataDescriptor.Length.Value;
                stream.DataLength.Value = dataDescriptor.Length.Value;
            }

            // now has value only if it was used before, so we spare a flag :)
            if (now.HasValue)
            {
                Update(entry);
            }
        }

        /// <summary>
        /// Creates a <see cref="ExFatFilesystemEntry" />.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="name">The name.</param>
        /// <param name="attributes">The attributes.</param>
        /// <returns></returns>
        private ExFatFilesystemEntry CreateEntry(ExFatFilesystemEntry parent, string name, FileAttributes attributes)
        {
            var now = DateTimeOffset.Now;
            var entries = new List<ExFatDirectoryEntry>
            {
                new FileExFatDirectoryEntry(new Buffer(new byte[32]))
                {
                    EntryType = {Value = ExFatDirectoryEntryType.File},
                    FileAttributes = {Value = (ExFatFileAttributes) attributes},
                    CreationDateTimeOffset = {Value = now},
                    LastWriteDateTimeOffset = {Value = now},
                    LastAccessDateTimeOffset = {Value = now},
                },
                new StreamExtensionExFatDirectoryEntry(new Buffer(new byte[32]))
                {
                    FirstCluster = {Value = 0},
                    EntryType = {Value = ExFatDirectoryEntryType.Stream},
                    GeneralSecondaryFlags = {Value = ExFatGeneralSecondaryFlags.ClusterAllocationPossible},
                    NameLength = {Value = (byte) name.Length},
                    NameHash = {Value = _partition.ComputeNameHash(name)},
                }
            };
            for (int nameIndex = 0; nameIndex < name.Length; nameIndex += 15)
            {
                var namePart = name.Substring(nameIndex, Math.Min(15, name.Length - nameIndex));
                entries.Add(new FileNameExtensionExFatDirectoryEntry(new Buffer(new byte[32]))
                {
                    EntryType = { Value = ExFatDirectoryEntryType.FileName },
                    FileName = { Value = namePart }
                });
            }
            var metaEntry = new ExFatMetaDirectoryEntry(entries);
            var entry = new ExFatFilesystemEntry(parent.DataDescriptor, metaEntry);
            return entry;
        }

        /// <summary>
        /// Creates a directory or returns the existing.
        /// </summary>
        /// <param name="parentDirectoryEntry">The parent directory.</param>
        /// <param name="directoryName">Name of the directory.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="IOException"></exception>
        public ExFatFilesystemEntry CreateDirectory(ExFatFilesystemEntry parentDirectoryEntry, string directoryName)
        {
            if (!parentDirectoryEntry.IsDirectory)
                throw new InvalidOperationException();

            lock (_lock)
            {
                var existingEntry = FindChild(parentDirectoryEntry, directoryName);
                if (existingEntry != null)
                {
                    if (!existingEntry.IsDirectory)
                        throw new IOException();
                    return existingEntry;
                }

                var directoryEntry = CreateEntry(parentDirectoryEntry, directoryName, FileAttributes.Directory);
                var updatedDataDescriptor = _partition.AddEntry(parentDirectoryEntry.DataDescriptor, directoryEntry.MetaEntry);
                using (var directoryStream = OpenData(directoryEntry, FileAccess.ReadWrite))
                {
                    // at least one empty entry, otherwise CHKDSK doesn't understand (the dumbass)
                    var empty = new byte[32];
                    directoryStream.Write(empty, 0, empty.Length);
                }
                UpdateEntry(parentDirectoryEntry, FileAccess.Write, updatedDataDescriptor);
                return directoryEntry;
            }
        }

        /// <summary>
        /// Deletes the specified entry.
        /// </summary>
        /// <param name="entry">The entry.</param>
        public void Delete(ExFatFilesystemEntry entry)
        {
            _partition.Deallocate(entry.DataDescriptor);
            foreach (var e in entry.MetaEntry.Entries)
                e.EntryType.Value &= ~ExFatDirectoryEntryType.InUse;
            Update(entry);
        }

        /// <summary>
        /// Deletes the tree for specified entry.
        /// </summary>
        /// <param name="entry">The entry.</param>
        public void DeleteTree(ExFatFilesystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                foreach (var childEntry in EnumerateFileSystemEntries(entry))
                    DeleteTree(childEntry);
            }
            Delete(entry);
        }

        /// <summary>
        /// Writes entry to partition (after changes).
        /// </summary>
        /// <param name="entry">The entry.</param>
        public void Update(ExFatFilesystemEntry entry)
        {
            _partition.UpdateEntry(entry.ParentDataDescriptor, entry.MetaEntry);
        }
    }
}