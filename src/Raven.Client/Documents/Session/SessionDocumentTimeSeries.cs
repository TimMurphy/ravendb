﻿//-----------------------------------------------------------------------
// <copyright file="SessionDocumentTimeSeries.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Session.Loaders;
using Raven.Client.Documents.Session.TimeSeries;
using Raven.Client.Util;

namespace Raven.Client.Documents.Session
{
    public sealed class SessionDocumentTimeSeries<TValues> : ISessionDocumentTimeSeries, ISessionDocumentRollupTypedTimeSeries<TValues>,
        ISessionDocumentTypedTimeSeries<TValues>, ISessionDocumentIncrementalTimeSeries, ISessionDocumentTypedIncrementalTimeSeries<TValues> where TValues : new()
    {
        private readonly AsyncSessionDocumentTimeSeries<TimeSeriesEntry> _asyncSessionTimeSeries;

        public SessionDocumentTimeSeries(InMemoryDocumentSessionOperations session, string documentId, string name)
        {
            _asyncSessionTimeSeries = new AsyncSessionDocumentTimeSeries<TimeSeriesEntry>(session, documentId, name);
        }

        public SessionDocumentTimeSeries(InMemoryDocumentSessionOperations session, object entity, string name)
        {
            _asyncSessionTimeSeries = new AsyncSessionDocumentTimeSeries<TimeSeriesEntry>(session, entity, name);
        }

        /// <inheritdoc cref="ISessionDocumentAppendTimeSeriesBase.Append"/>
        void ISessionDocumentAppendTimeSeriesBase.Append(DateTime timestamp, IEnumerable<double> values, string tag)
        {
            _asyncSessionTimeSeries.Append(timestamp, values, tag);
        }

        /// <inheritdoc cref="ISessionDocumentAppendTimeSeriesBase.Append(DateTime, double, string)"/>
        void ISessionDocumentAppendTimeSeriesBase.Append(DateTime timestamp, double value, string tag)
        {
            _asyncSessionTimeSeries.Append(timestamp, value, tag);
        }

        /// <inheritdoc cref="ISessionDocumentTypedAppendTimeSeriesBase{TValue}.Append"/>
        void ISessionDocumentTypedAppendTimeSeriesBase<TValues>.Append(DateTime timestamp, TValues entry, string tag)
        {
            _asyncSessionTimeSeries.Append(timestamp, entry, tag);
        }

        /// <inheritdoc cref="ISessionDocumentTypedAppendTimeSeriesBase{TValue}.Append(TimeSeriesEntry{TValue})"/>
        void ISessionDocumentTypedAppendTimeSeriesBase<TValues>.Append(TimeSeriesEntry<TValues> entry)
        {
            _asyncSessionTimeSeries.Append(entry.Timestamp, entry.Value, entry.Tag);
        }

        /// <inheritdoc cref="ISessionDocumentTimeSeries.Get"/>
        public TimeSeriesEntry[] Get(DateTime? from = null, DateTime? to = null, int start = 0, int pageSize = int.MaxValue)
        {
            return Get(from, to, includes: null, start, pageSize);
        }

        /// <inheritdoc cref="ISessionDocumentTimeSeries.Get(DateTime?, DateTime?, Action{ITimeSeriesIncludeBuilder}, int, int)"/>
        public TimeSeriesEntry[] Get(DateTime? from, DateTime? to, Action<ITimeSeriesIncludeBuilder> includes, int start = 0, int pageSize = int.MaxValue)
        {
            return AsyncHelpers.RunSync(() => _asyncSessionTimeSeries.GetAsync(from, to, includes, start, pageSize));
        }

        private TimeSeriesEntry<TValues>[] GetInternal(DateTime? from, DateTime? to, int start, int pageSize)
        {
            return AsyncHelpers.RunSync(() =>
            {
                if (_asyncSessionTimeSeries.NotInCache(from, to))
                {
                    return _asyncSessionTimeSeries.GetTimeSeriesAndIncludes<TimeSeriesEntry<TValues>>(from, to, includes: null, start, pageSize);
                }

                return _asyncSessionTimeSeries.GetTypedFromCache<TValues>(from, to, null, start, pageSize);
            });
        }

        /// <inheritdoc cref="ISessionDocumentTypedTimeSeries{TValue}.Get"/>
        TimeSeriesEntry<TValues>[] ISessionDocumentTypedTimeSeries<TValues>.Get(DateTime? from, DateTime? to, int start, int pageSize)
        {
            return GetInternal(from, to, start, pageSize);
        }

        /// <inheritdoc cref="ISessionDocumentRollupTypedTimeSeries{TValue}.Get"/>
        TimeSeriesRollupEntry<TValues>[] ISessionDocumentRollupTypedTimeSeries<TValues>.Get(DateTime? from, DateTime? to, int start, int pageSize)
        {
            if (_asyncSessionTimeSeries.NotInCache(from, to))
            {
                return AsyncHelpers.RunSync(() => _asyncSessionTimeSeries.GetTimeSeriesAndIncludes<TimeSeriesRollupEntry<TValues>>(from, to, includes: null, start, pageSize));
            }

            var result = _asyncSessionTimeSeries.GetTypedFromCache<TValues>(from, to, includes: null, start, pageSize);
            return result.Result?.Select(r => r.AsRollupEntry()).ToArray();
        }

        /// <inheritdoc cref="ISessionDocumentRollupTypedAppendTimeSeriesBase{TValue}.Append"/>
        public void Append(TimeSeriesRollupEntry<TValues> entry)
        {
            entry.SetValuesFromMembers();
            _asyncSessionTimeSeries.Append(entry.Timestamp, entry.Values, entry.Tag);
        }

        /// <inheritdoc cref="ISessionDocumentTypedIncrementalTimeSeries{TValue}.Get"/>
        TimeSeriesEntry<TValues>[] ISessionDocumentTypedIncrementalTimeSeries<TValues>.Get(DateTime? from, DateTime? to, int start, int pageSize)
        {
            return GetInternal(from, to, start, pageSize);
        }

        /// <inheritdoc cref="ISessionDocumentDeleteTimeSeriesBase.Delete"/>
        void ISessionDocumentDeleteTimeSeriesBase.Delete(DateTime? from, DateTime? to)
        {
            _asyncSessionTimeSeries.Delete(from, to);
        }

        /// <inheritdoc cref="ISessionDocumentDeleteTimeSeriesBase.Delete(DateTime)"/>
        void ISessionDocumentDeleteTimeSeriesBase.Delete(DateTime at)
        {
            _asyncSessionTimeSeries.Delete(at);
        }

        /// <inheritdoc/>
        IEnumerator<TimeSeriesEntry> ITimeSeriesStreamingBase<TimeSeriesEntry>.Stream(DateTime? @from, DateTime? to, TimeSpan? offset)
        {
            return AsyncHelpers.RunSync(() => _asyncSessionTimeSeries.GetStream<TimeSeriesEntry>(from, to, offset));
        }

        /// <inheritdoc/>
        IEnumerator<TimeSeriesRollupEntry<TValues>> ITimeSeriesStreamingBase<TimeSeriesRollupEntry<TValues>>.Stream(DateTime? from, DateTime? to, TimeSpan? offset)
        {
            return AsyncHelpers.RunSync(() => _asyncSessionTimeSeries.GetStream<TimeSeriesRollupEntry<TValues>>(from, to, offset));
        }

        /// <inheritdoc/>
        IEnumerator<TimeSeriesEntry<TValues>> ITimeSeriesStreamingBase<TimeSeriesEntry<TValues>>.Stream(DateTime? from, DateTime? to, TimeSpan? offset)
        {
            return AsyncHelpers.RunSync(() => _asyncSessionTimeSeries.GetStream<TimeSeriesEntry<TValues>>(from, to, offset));
        }

        private void Increment(DateTime timestamp, IEnumerable<double> values)
        {
            _asyncSessionTimeSeries.Increment(timestamp, values);
        }

        /// <inheritdoc cref="ISessionDocumentIncrementTimeSeriesBase.Increment"/>
        void ISessionDocumentIncrementTimeSeriesBase.Increment(DateTime timestamp, IEnumerable<double> values)
        {
            Increment(timestamp, values);
        }

        /// <inheritdoc cref="ISessionDocumentIncrementTimeSeriesBase.Increment"/>
        void ISessionDocumentIncrementTimeSeriesBase.Increment(IEnumerable<double> values)
        {
            Increment(DateTime.UtcNow, values);
        }

        /// <inheritdoc cref="ISessionDocumentIncrementTimeSeriesBase.Increment(DateTime, double)"/>
        void ISessionDocumentIncrementTimeSeriesBase.Increment(DateTime timestamp, double value)
        {
            Increment(timestamp, new[] { value });
        }

        /// <inheritdoc cref="ISessionDocumentIncrementTimeSeriesBase.Increment(double)"/>
        void ISessionDocumentIncrementTimeSeriesBase.Increment(double value)
        {
            Increment(DateTime.UtcNow, new[] { value });
        }

        /// <inheritdoc cref="ISessionDocumentTypedIncrementTimeSeriesBase{TValues}.Increment"/>
        void ISessionDocumentTypedIncrementTimeSeriesBase<TValues>.Increment(DateTime timestamp, TValues entry)
        {
            _asyncSessionTimeSeries.Increment(timestamp, entry);
        }

        /// <inheritdoc cref="ISessionDocumentTypedIncrementTimeSeriesBase{TValues}.Increment"/>
        void ISessionDocumentTypedIncrementTimeSeriesBase<TValues>.Increment(TValues entry)
        {
            _asyncSessionTimeSeries.Increment(DateTime.UtcNow, entry);
        }
    }
}
