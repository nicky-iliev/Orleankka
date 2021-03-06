﻿using System;
using System.Diagnostics;

using Orleans.Concurrency;

namespace Orleankka
{
    using Utility;

    [Serializable, Immutable]
    [DebuggerDisplay("{ToString()}")]
    public struct StreamPath : IEquatable<StreamPath>
    {
        public static readonly StreamPath Empty = new StreamPath();
        public static readonly string[] Separator = {":"};

        public static StreamPath From(string provider, string id)
        {
            Requires.NotNull(provider, nameof(provider));
            Requires.NotNull(id, nameof(id));
            Requires.NotNullOrWhitespace(id, nameof(id));

            return new StreamPath(provider, id);
        }

        public static StreamPath Parse(string path)
        {
            Requires.NotNull(path, nameof(path));

            var parts = path.Split(Separator, 2, StringSplitOptions.None);
            if (parts.Length != 2)
                throw new ArgumentException("Invalid stream path: " + path);

            var provider = parts[0];
            var id = parts[1];

            return new StreamPath(provider, id);
        }

        public readonly string Provider;
        public readonly string Id;

        StreamPath(string provider, string id)
        {
            Provider = provider;
            Id = id;
        }

        public bool Equals(StreamPath other) => Provider == other.Provider && string.Equals(Id, other.Id);
        public override bool Equals(object obj) => !ReferenceEquals(null, obj) && (obj is StreamPath && Equals((StreamPath)obj));

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Provider?.GetHashCode() ?? 0) * 397) ^ (Id?.GetHashCode() ?? 0);
            }
        }

        public static implicit operator string(StreamPath arg) => arg.ToString();

        public static bool operator ==(StreamPath left, StreamPath right) => left.Equals(right);
        public static bool operator !=(StreamPath left, StreamPath right) => !left.Equals(right);

        public override string ToString() => $"{Provider}:{Id}";
    }
}
