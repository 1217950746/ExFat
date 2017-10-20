﻿// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace ExFat.IO
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Information about data in partition
    /// </summary>
    [DebuggerDisplay("@{FirstCluster.Value} ({Length}) contiguous={Contiguous}")]
    public class DataDescriptor
    {
        /// <summary>
        /// Gets the first cluster of the data in partition.
        /// </summary>
        /// <value>
        /// The first cluster.
        /// </value>
        public Cluster FirstCluster { get; }

        /// <summary>
        /// Gets a value indicating whether this <see cref="DataDescriptor"/> is contiguous.
        /// When data is contiguous there is no need to read the FAT information
        /// </summary>
        /// <value>
        ///   <c>true</c> if contiguous; otherwise, <c>false</c>.
        /// </value>
        public bool Contiguous { get; }

        /// <summary>
        /// Gets the length. Can be null if the data is not contiguous (so the length is marked by last cluster)
        /// </summary>
        /// <value>
        /// The length.
        /// </value>
        public ulong Length { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DataDescriptor"/> class.
        /// </summary>
        /// <param name="firstCluster">The first cluster.</param>
        /// <param name="contiguous">if set to <c>true</c> [contiguous].</param>
        /// <param name="length">The length.</param>
        /// <exception cref="ArgumentException">length must be provided for contiguous streams</exception>
        public DataDescriptor(Cluster firstCluster, bool contiguous, ulong length)
        {
            FirstCluster = firstCluster;
            Contiguous = contiguous;
            Length = length;
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (!(obj is DataDescriptor other))
                return false;
            return FirstCluster == other.FirstCluster && Contiguous == other.Contiguous && Length == other.Length;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return FirstCluster.Value.GetHashCode() ^ Contiguous.GetHashCode() ^ Length.GetHashCode();
        }
    }
}