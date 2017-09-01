﻿namespace ExFat.Core
{
    using System;
    using System.IO;

    /// <summary>
    /// The ExFAT filesystem.
    /// The class is a quite low-level manipulator
    /// TODO: come up with a better name :)
    /// </summary>
    public class ExFatFS : IClusterInformationReader, IPartitionReader
    {
        public Stream PartitionStream { get; }
        public ExFatBootSector BootSector { get; }

        public long SectorsPerCluster => 1 << BootSector.SectorsPerCluster.Value;
        public int BytesPerSector => 1 << BootSector.BytesPerSector.Value;

        public int BytesPerCluster => 1 << (BootSector.BytesPerSector.Value + BootSector.BytesPerSector.Value);

        public ExFatFS(Stream partitionStream)
        {
            PartitionStream = partitionStream;
            BootSector = ReadBootSector(PartitionStream);
        }

        public static ExFatBootSector ReadBootSector(Stream partitionStream)
        {
            partitionStream.Seek(0, SeekOrigin.Begin);
            var bootSector = new ExFatBootSector();
            bootSector.Read(partitionStream);
            return bootSector;
        }

        public long GetClusterOffset(long clusterIndex)
        {
            return (BootSector.ClusterOffset.Value + (clusterIndex - 2) * SectorsPerCluster) * BytesPerSector;
        }

        public void SeekCluster(long clusterIndex)
        {
            PartitionStream.Seek(GetClusterOffset(clusterIndex), SeekOrigin.Begin);
        }

        public long GetSectorOffset(long sectorIndex)
        {
            return sectorIndex * BytesPerSector;
        }

        public void SeekSector(long sectorIndex)
        {
            PartitionStream.Seek(GetSectorOffset(sectorIndex), SeekOrigin.Begin);
        }

        private long _fatPageIndex = -1;
        private byte[] _fatPage;
        private const int SectorsPerFatPage = 1;
        private int FatPageSize => BytesPerSector * SectorsPerFatPage;
        private int ClustersPerFatPage => FatPageSize / sizeof(Int32);

        private byte[] GetFatPage(long cluster)
        {
            if (_fatPage == null)
                _fatPage = new byte[FatPageSize];

            var fatPageIndex = cluster / ClustersPerFatPage;
            if (fatPageIndex != _fatPageIndex)
            {
                ReadSectors(BootSector.FatOffset.Value + fatPageIndex * SectorsPerFatPage, _fatPage, SectorsPerFatPage);
                _fatPageIndex = fatPageIndex;
            }
            return _fatPage;
        }

        public long GetNext(long cluster)
        {
            // TODO: optimize... A lot!
            var fatPage = GetFatPage(cluster);
            var clusterIndex = cluster % ClustersPerFatPage;
            return BitConverter.ToInt32(fatPage, (int)clusterIndex * sizeof(Int32));
        }

        public void ReadCluster(long cluster, byte[] clusterBuffer)
        {
            SeekCluster(cluster);
            PartitionStream.Read(clusterBuffer, 0, BytesPerCluster);
        }

        public void ReadSectors(long sector, byte[] sectorBuffer, int sectorCount)
        {
            SeekSector(sector);
            PartitionStream.Read(sectorBuffer, 0, BytesPerSector * sectorCount);
        }
    }
}
