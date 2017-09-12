﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.Partition
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Entries;
    using IO;

    /// <summary>
    /// The ExFAT filesystem.
    /// The class is a quite low-level accessor
    /// </summary>
    public partial class ExFatPartition : IClusterWriter, IDisposable
    {
        private readonly Stream _partitionStream;
        private readonly object _streamLock = new object();

        /// <summary>
        /// Gets the boot sector.
        /// </summary>
        /// <value>
        /// The boot sector.
        /// </value>
        public ExFatBootSector BootSector { get; private set; }

        /// <inheritdoc />
        /// <summary>
        /// Gets the cluster size, in bytes
        /// </summary>
        /// <value>
        /// The bytes per cluster.
        /// </value>
        public int BytesPerCluster => (int)(BootSector.SectorsPerCluster.Value * BootSector.BytesPerSector.Value);

        /// <summary>
        /// Gets the root directory data descriptor.
        /// </summary>
        /// <value>
        /// The root directory data descriptor.
        /// </value>
        public DataDescriptor RootDirectoryDataDescriptor => new DataDescriptor(BootSector.RootDirectoryCluster.Value, false, long.MaxValue);

        /// <summary>
        /// Gets the total size.
        /// </summary>
        /// <value>
        /// The total size.
        /// </value>
        public long TotalSpace => BootSector.ClusterCount.Value * (long)BytesPerCluster;

        /// <summary>
        /// Gets the used space
        /// </summary>
        /// <value>
        /// The used space.
        /// </value>
        public long UsedSpace => GetUsedClusters() * (long)BytesPerCluster;

        /// <summary>
        /// Gets the available size.
        /// </summary>
        /// <value>
        /// The available size.
        /// </value>
        public long AvailableSpace => TotalSpace - UsedSpace;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExFatPartition"/> class.
        /// </summary>
        /// <param name="partitionStream">The partition stream.</param>
        /// <exception cref="System.ArgumentException">
        /// Given stream must be seekable
        /// or
        /// Given stream must be readable
        /// </exception>
        public ExFatPartition(Stream partitionStream)
            : this(partitionStream, true)
        {
        }

        private ExFatPartition(Stream partitionStream, bool readBootsector)
        {
            if (!partitionStream.CanSeek)
                throw new ArgumentException("Given stream must be seekable");
            if (!partitionStream.CanRead)
                throw new ArgumentException("Given stream must be readable");

            _partitionStream = partitionStream;
            if (readBootsector)
                BootSector = ReadBootSector(_partitionStream);
        }

        /// <summary>
        /// Releases (actually flushes) all pending resources (which are actually already flushed).
        /// </summary>
        public void Dispose()
        {
            Flush();
            DisposeAllocationBitmap();
        }

        /// <summary>
        /// Disposes the allocation bitmap.
        /// </summary>
        private void DisposeAllocationBitmap()
        {
            _allocationBitmap?.Dispose();
        }

        /// <summary>
        /// Flushes all pending changes.
        /// </summary>
        public void Flush()
        {
            FlushAllocationBitmap();
            FlushFatPage();
            FlushPartitionStream();
        }

        /// <summary>
        /// Flushes the partition stream.
        /// </summary>
        private void FlushPartitionStream()
        {
            // because .Flush() is not implemented in DiscUtils :)
            try
            {
                _partitionStream.Flush();
            }
            catch (SystemException) // because I want to catch the NotImplemented.Exception but R# considers it as a TO.DO
            {
            }
        }

        /// <summary>
        /// Flushes the allocation bitmap.
        /// </summary>
        private void FlushAllocationBitmap()
        {
            _allocationBitmap?.Flush();
        }

        /// <summary>
        /// Reads the boot sector.
        /// </summary>
        /// <param name="partitionStream">The partition stream.</param>
        /// <returns></returns>
        public static ExFatBootSector ReadBootSector(Stream partitionStream)
        {
            partitionStream.Seek(0, SeekOrigin.Begin);
            var defaultBootSector = new ExFatBootSector(new byte[512]);
            defaultBootSector.Read(partitionStream);
            var sectorSize = defaultBootSector.BytesPerSector.Value;

            // it probably not a valid exFAT boot sector, so don't dig any further
            if (sectorSize < 512 || sectorSize > 4096)
                return defaultBootSector;

            var fullData = new byte[sectorSize * 12];

            partitionStream.Seek(0, SeekOrigin.Begin);
            var bootSector = new ExFatBootSector(fullData);
            bootSector.Read(partitionStream);
            return bootSector;
        }

        /// <summary>
        /// Gets the cluster offset.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <returns></returns>
        public long GetClusterOffset(Cluster cluster)
        {
            return (BootSector.ClusterOffsetSector.Value + (cluster.Value - 2) * BootSector.SectorsPerCluster.Value) * BootSector.BytesPerSector.Value;
        }

        /// <summary>
        /// Seeks the cluster.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="offset">The offset.</param>
        private void SeekCluster(Cluster cluster, long offset = 0)
        {
            _partitionStream.Seek(GetClusterOffset(cluster) + offset, SeekOrigin.Begin);
        }

        /// <summary>
        /// Gets the sector offset.
        /// </summary>
        /// <param name="sectorIndex">Index of the sector.</param>
        /// <returns></returns>
        public long GetSectorOffset(long sectorIndex)
        {
            return sectorIndex * (int)BootSector.BytesPerSector.Value;
        }

        /// <summary>
        /// Seeks the sector.
        /// </summary>
        /// <param name="sectorIndex">Index of the sector.</param>
        private void SeekSector(long sectorIndex)
        {
            _partitionStream.Seek(GetSectorOffset(sectorIndex), SeekOrigin.Begin);
        }

        private long _fatPageIndex = -1;
        private byte[] _fatPage;
        private bool _fatPageDirty;
        private const int SectorsPerFatPage = 1;
        private int FatPageSize => (int)BootSector.BytesPerSector.Value * SectorsPerFatPage;
        private int ClustersPerFatPage => FatPageSize / sizeof(Int32);

        private byte[] GetFatPage(Cluster cluster)
        {
            if (_fatPage == null)
                _fatPage = new byte[FatPageSize];

            var fatPageIndex = cluster.Value / ClustersPerFatPage;
            if (fatPageIndex != _fatPageIndex)
            {
                FlushFatPage();
                ReadSectors(BootSector.FatOffsetSector.Value + fatPageIndex * SectorsPerFatPage, _fatPage, SectorsPerFatPage);
                _fatPageIndex = fatPageIndex;
            }

            return _fatPage;
        }

        private void FlushFatPage()
        {
            if (_fatPage != null && _fatPageDirty)
            {
                // write first fat
                WriteSectors(BootSector.FatOffsetSector.Value + _fatPageIndex * SectorsPerFatPage, _fatPage, SectorsPerFatPage);
                // optionnally update second
                if (BootSector.NumberOfFats.Value == 2)
                    WriteSectors(BootSector.FatOffsetSector.Value + BootSector.FatLengthSectors.Value + _fatPageIndex * SectorsPerFatPage, _fatPage, SectorsPerFatPage);
                _fatPageDirty = false;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Gets the next cluster for a given cluster.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <returns></returns>
        public Cluster GetNextCluster(Cluster cluster)
        {
            // TODO: optimize?
            lock (_streamLock)
            {
                var fatPage = GetFatPage(cluster);
                var clusterIndex = (int)(cluster.Value % ClustersPerFatPage);
                var nextCluster = LittleEndian.ToUInt32(fatPage, clusterIndex * sizeof(Int32));
                return nextCluster;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Gets all clusters from a <see cref="T:ExFat.IO.DataDescriptor" />.
        /// </summary>
        /// <param name="dataDescriptor">The data descriptor.</param>
        /// <returns></returns>
        public IEnumerable<Cluster> GetClusters(DataDescriptor dataDescriptor)
        {
            var cluster = dataDescriptor.FirstCluster;
            var length = (long?)dataDescriptor.Length ?? long.MaxValue;
            for (long offset = 0; offset < length; offset += BytesPerCluster)
            {
                if (cluster.IsLast)
                    yield break;
                yield return cluster;
                if (dataDescriptor.Contiguous)
                    cluster = cluster + 1;
                else
                    cluster = GetNextCluster(cluster);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Sets the next cluster.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="nextCluster">The next cluster.</param>
        public void SetNextCluster(Cluster cluster, Cluster nextCluster)
        {
            lock (_streamLock)
            {
                var fatPage = GetFatPage(cluster);
                var clusterIndex = (int)(cluster.Value % ClustersPerFatPage);
                LittleEndian.GetBytes((UInt32)nextCluster.Value, fatPage, clusterIndex * sizeof(Int32));
                _fatPageDirty = true;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Allocates a cluster.
        /// </summary>
        /// <param name="previousClusterHint">A hint about the previous cluster, this allows to allocate the next one, if available</param>
        /// <returns></returns>
        public Cluster AllocateCluster(Cluster previousClusterHint)
        {
            var allocationBitmap = GetAllocationBitmap();
            lock (_streamLock)
            {
                Cluster cluster;
                // no data? anything else is good
                if (!previousClusterHint.IsData)
                    cluster = allocationBitmap.FindUnallocated();
                else
                {
                    // try next
                    cluster = previousClusterHint + 1;
                    if (allocationBitmap[cluster])
                        cluster = allocationBitmap.FindUnallocated();
                }
                allocationBitmap[cluster] = true;
                return cluster;
            }
        }

        /// <summary>
        /// Frees the specified <see cref="DataDescriptor"/> clusters.
        /// </summary>
        /// <param name="dataDescriptor">The data descriptor.</param>
        public void Deallocate(DataDescriptor dataDescriptor)
        {
            var allocationBitmap = GetAllocationBitmap();
            // TODO: optimize to write all only once
            foreach (var cluster in GetClusters(dataDescriptor))
                allocationBitmap[cluster] = false;
        }

        /// <inheritdoc />
        /// <summary>
        /// Frees the cluster.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        public void FreeCluster(Cluster cluster)
        {
            lock (_streamLock)
            {
                GetAllocationBitmap()[cluster] = false;
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Reads one cluster.
        /// </summary>
        /// <param name="cluster">The cluster number.</param>
        /// <param name="clusterBuffer">The cluster buffer. It must be large enough to contain full cluster</param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// length
        /// or
        /// length
        /// or
        /// offset
        /// </exception>
        public void ReadCluster(Cluster cluster, byte[] clusterBuffer, int offset, int length)
        {
            if (length + offset > BytesPerCluster)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            lock (_streamLock)
            {
                SeekCluster(cluster);
                _partitionStream.Read(clusterBuffer, offset, length);
            }
        }

        /// <inheritdoc />
        /// <summary>
        /// Writes the cluster.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="clusterBuffer">The cluster buffer.</param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// length
        /// or
        /// length
        /// or
        /// offset
        /// </exception>
        public void WriteCluster(Cluster cluster, byte[] clusterBuffer, int offset, int length)
        {
            if (length + offset > BytesPerCluster)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            lock (_streamLock)
            {
                SeekCluster(cluster);
                _partitionStream.Write(clusterBuffer, offset, length);
            }
        }

        /// <summary>
        /// Reads the sectors.
        /// </summary>
        /// <param name="sector">The sector.</param>
        /// <param name="sectorBuffer">The sector buffer.</param>
        /// <param name="sectorCount">The sector count.</param>
        public void ReadSectors(long sector, byte[] sectorBuffer, int sectorCount)
        {
            lock (_streamLock)
            {
                SeekSector(sector);
                _partitionStream.Read(sectorBuffer, 0, (int)BootSector.BytesPerSector.Value * sectorCount);
            }
        }

        /// <summary>
        /// Writes the sectors.
        /// </summary>
        /// <param name="sector">The sector.</param>
        /// <param name="sectorBuffer">The sector buffer.</param>
        /// <param name="sectorCount">The sector count.</param>
        public void WriteSectors(long sector, byte[] sectorBuffer, int sectorCount)
        {
            lock (_streamLock)
            {
                SeekSector(sector);
                _partitionStream.Write(sectorBuffer, 0, (int)BootSector.BytesPerSector.Value * sectorCount);
            }
        }

        /// <summary>
        /// Gets the name hash.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
        public UInt16 ComputeNameHash(string name)
        {
            UInt16 hash = 0;
            var upCaseTable = GetUpCaseTable();
            foreach (var c in name)
            {
                var uc = upCaseTable.ToUpper(c);
                hash = (UInt16)(hash.RotateRight() + (uc & 0xFF));
                hash = (UInt16)(hash.RotateRight() + (uc >> 8));
            }
            return hash;
        }

        /// <summary>
        /// Opens a clusters stream.
        /// </summary>
        /// <param name="dataDescriptor">The data descriptor.</param>
        /// <param name="fileAccess">The file access.</param>
        /// <param name="onDisposed">Method invoked when stream is disposed.</param>
        /// <returns></returns>
        private ClusterStream OpenClusterStream(DataDescriptor dataDescriptor, FileAccess fileAccess, Action<DataDescriptor> onDisposed = null)
        {
            if (fileAccess == FileAccess.Read)
                return new ClusterStream(this, null, dataDescriptor, onDisposed);
            // write and read/write will be the same
            return new ClusterStream(this, this, dataDescriptor, onDisposed);
        }

        /// <summary>
        /// Opens the data stream.
        /// </summary>
        /// <param name="dataDescriptor">The data descriptor.</param>
        /// <param name="fileAccess">The file access.</param>
        /// <param name="onDisposed">Method invoked when stream is disposed.</param>
        /// <returns></returns>
        public ClusterStream OpenDataStream(DataDescriptor dataDescriptor, FileAccess fileAccess, Action<DataDescriptor> onDisposed = null)
        {
            if (dataDescriptor == null)
                return null;
            return OpenClusterStream(dataDescriptor, fileAccess, onDisposed);
        }

        /// <summary>
        /// Creates the data stream.
        /// </summary>
        /// <param name="onDisposed">The on disposed.</param>
        /// <returns></returns>
        public ClusterStream CreateDataStream(Action<DataDescriptor> onDisposed = null)
        {
            return OpenClusterStream(new DataDescriptor(0, true, 0), FileAccess.ReadWrite, onDisposed);
        }

        private IEnumerable<TDirectoryEntry> FindRootDirectoryEntries<TDirectoryEntry>()
            where TDirectoryEntry : ExFatDirectoryEntry
        {
            return GetEntries(RootDirectoryDataDescriptor).OfType<TDirectoryEntry>();
        }

        private ExFatUpCaseTable _upCaseTable;

        /// <summary>
        /// Gets up case table.
        /// </summary>
        /// <returns></returns>
        public ExFatUpCaseTable GetUpCaseTable()
        {
            if (_upCaseTable == null)
            {
                _upCaseTable = new ExFatUpCaseTable();
                var upCaseTableEntry = FindRootDirectoryEntries<UpCaseTableExFatDirectoryEntry>().FirstOrDefault();
                if (upCaseTableEntry != null)
                {
                    using (var upCaseTableStream = OpenDataStream(upCaseTableEntry.DataDescriptor, FileAccess.Read))
                        _upCaseTable.Read(upCaseTableStream);
                }
                else
                    _upCaseTable.SetDefault();
            }
            return _upCaseTable;
        }

        private ExFatAllocationBitmap _allocationBitmap;

        /// <summary>
        /// Gets the allocation bitmap.
        /// </summary>
        /// <returns></returns>
        public ExFatAllocationBitmap GetAllocationBitmap()
        {
            if (_allocationBitmap == null)
            {
                _allocationBitmap = new ExFatAllocationBitmap();
                var allocationBitmapEntry = FindRootDirectoryEntries<AllocationBitmapExFatDirectoryEntry>()
                    .First(b => !b.BitmapFlags.Value.HasAny(AllocationBitmapFlags.SecondClusterBitmap));
                var allocationBitmapStream = OpenDataStream(allocationBitmapEntry.DataDescriptor, FileAccess.ReadWrite);
                _allocationBitmap.Open(allocationBitmapStream, allocationBitmapEntry.FirstCluster.Value, BootSector.ClusterCount.Value);
            }
            return _allocationBitmap;
        }

        /// <summary>
        /// Gets the used clusters.
        /// </summary>
        /// <returns></returns>
        private long GetUsedClusters()
        {
            return GetAllocationBitmap().GetUsedClusters();
        }
    }
}