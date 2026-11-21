using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VLat.Core.Model
{
    internal static class ImmutableModelCollection
    {
        public static IReadOnlyList<T> Copy<T>(IEnumerable<T> source)
        {
            if (source == null)
            {
                return Array.AsReadOnly(Array.Empty<T>());
            }

            var collection = source as ICollection<T>;
            if (collection != null)
            {
                var copy = new T[collection.Count];
                collection.CopyTo(copy, 0);
                return Array.AsReadOnly(copy);
            }

            var items = new List<T>();
            foreach (var item in source)
            {
                items.Add(item);
            }

            return new ReadOnlyCollection<T>(items);
        }
    }
}
