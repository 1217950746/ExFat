﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.Partition
{
    using System;
    using System.IO;
    using IO;

    public class ExFatAllocationBitmap
    {
        private byte[] _bitmap;
        private Stream _dataStream;
        private uint _firstCluster;

        public long Length { get; private set; }

        /// <summary>
        /// Gets or sets the allocation state fpr the specified cluster.
        /// </summary>
        /// <value>
        /// The <see cref="System.Boolean"/>.
        /// </value>
        /// <param name="cluster">The cluster.</param>
        /// <returns></returns>
        public bool this[Cluster cluster]
        {
            get { return GetAt(cluster); }
            set { SetAt(cluster, value); }
        }

        /// <summary>
        /// Opens the specified data stream.
        /// </summary>
        /// <param name="dataStream">The data stream.</param>
        /// <param name="firstCluster">The first cluster.</param>
        /// <param name="length">The length.</param>
        public void Open(Stream dataStream, uint firstCluster, long length)
        {
            _dataStream = dataStream;
            _firstCluster = firstCluster;
            _bitmap = new byte[dataStream.Length];
            dataStream.Read(_bitmap, 0, _bitmap.Length);
            Length = length;
        }

        public void Flush()
        {
            _dataStream.Seek(0, SeekOrigin.Begin);
            _dataStream.Write(_bitmap, 0, _bitmap.Length);
            _dataStream.Flush();
        }

        public void Dispose()
        {
            Flush();
            _dataStream.Dispose();
        }

        public bool GetAt(Cluster cluster)
        {
            if (cluster.Value < _firstCluster || cluster.Value >= Length)
                throw new ArgumentOutOfRangeException(nameof(cluster));
            var clusterIndex = cluster.Value - _firstCluster;
            var byteIndex = (int)clusterIndex / 8;
            var bitMask = 1 << (int)(clusterIndex & 7);
            return (_bitmap[byteIndex] & bitMask) != 0;
        }

        public void SetAt(Cluster cluster, bool allocated)
        {
            if (cluster.Value < _firstCluster || cluster.Value >= Length)
                throw new ArgumentOutOfRangeException(nameof(cluster));
            var clusterIndex = cluster.Value - _firstCluster;
            var byteIndex = (int)clusterIndex / 8;
            var bitMask = 1 << (int)(clusterIndex & 7);
            if (allocated)
                _bitmap[byteIndex] |= (byte)bitMask;
            else
                _bitmap[byteIndex] &= (byte)~bitMask;
            // for some unknown reason, this does not work on DiscUtils, so the Flush() handle all problems
            _dataStream.Seek(byteIndex, SeekOrigin.Begin);
            _dataStream.Write(_bitmap, byteIndex, 1);
        }

        public Cluster FindUnallocated(int contiguous = 1)
        {
            UInt32 freeCluster = 0;
            int unallocatedCount = 0;
            for (UInt32 cluster = _firstCluster; cluster < Length;)
            {
                // special case: byte is filled, skip the block (and reset the search)
                if (((cluster - _firstCluster) & 0x07) == 0 && _bitmap[cluster / 8] == 0xFF)
                {
                    freeCluster = 0;
                    unallocatedCount = 0;
                    cluster += 8;
                    continue;
                }
                // if it's free, count it
                if (!GetAt(cluster))
                {
                    // first to be free, keep it
                    if (unallocatedCount == 0)
                        freeCluster = cluster;
                    unallocatedCount++;

                    // when the amount is reached, return it
                    if (unallocatedCount == contiguous)
                        return freeCluster;
                }
                ++cluster;
            }
            // nothing found
            return Cluster.Free;
        }
    }
}