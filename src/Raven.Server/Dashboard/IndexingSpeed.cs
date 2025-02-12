﻿using System.Collections.Generic;
using Sparrow.Json.Parsing;

namespace Raven.Server.Dashboard
{
    public sealed class IndexingSpeed : AbstractDashboardNotification
    {
        public List<IndexingSpeedItem> Items { get; set; }

        public IndexingSpeed()
        {
            Items = new List<IndexingSpeedItem>();
        }
    }

    public sealed class IndexingSpeedItem : IDynamicJson
    {
        public string Database { get; set; }

        public double IndexedPerSecond { get; set; }

        public double MappedPerSecond { get; set; }

        public double ReducedPerSecond { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Database)] = Database,
                [nameof(IndexedPerSecond)] = IndexedPerSecond,
                [nameof(MappedPerSecond)] = MappedPerSecond,
                [nameof(ReducedPerSecond)] = ReducedPerSecond
            };
        }
    }
}
