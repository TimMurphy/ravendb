﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Corax;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Lucene.Net.Documents;
using Lucene.Net.Store;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Raven.Client.Exceptions;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Indexes.Persistence.Lucene.Documents;
using Raven.Server.Documents.Patch;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Documents.Queries.Results.TimeSeries;
using Raven.Server.Documents.Queries.Timings;
using Raven.Server.Json;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Server.Json.Sync;
using Constants = Raven.Client.Constants;
using IndexField = Raven.Server.Documents.Indexes.IndexField;
using RangeType = Raven.Client.Documents.Indexes.RangeType;

namespace Raven.Server.Documents.Queries.Results
{
    public abstract class QueryResultRetrieverCommon
    {
        public static readonly int LoadedDocumentsCacheSize = 16 * 1024;

        public static readonly Lucene.Net.Search.ScoreDoc ZeroScore = new Lucene.Net.Search.ScoreDoc(-1, 0f);

        public static readonly Lucene.Net.Search.ScoreDoc OneScore = new Lucene.Net.Search.ScoreDoc(-1, 1f);

        
        internal static FieldType GetFieldType(string field, Lucene.Net.Documents.Document indexDocument)
        {
            var isArray = false;
            var isJson = false;
            var isNumeric = false;

            var arrayFieldName = field + LuceneDocumentConverterBase.IsArrayFieldSuffix;
            var jsonConvertFieldName = field + LuceneDocumentConverterBase.ConvertToJsonSuffix;
            var numericFieldName = field + Constants.Documents.Indexing.Fields.RangeFieldSuffixDouble;

            foreach (var f in indexDocument.GetFields())
            {
                if (f.Name == arrayFieldName)
                {
                    isArray = true;
                    continue;
                }

                if (f.Name == jsonConvertFieldName)
                {
                    isJson = true;
                    break;
                }

                if (f.Name == numericFieldName)
                {
                    isNumeric = true;
                }
            }

            return new FieldType
            {
                IsArray = isArray,
                IsJson = isJson,
                IsNumeric = isNumeric
            };
        }

        internal class FieldType
        {
            public bool IsArray;
            public bool IsJson;
            public bool IsNumeric;
        }
    }
    
    public abstract class QueryResultRetrieverBase<TDocument> : QueryResultRetrieverCommon, IQueryResultRetriever<TDocument>
    where TDocument : Document, new()
    {

        private readonly DocumentDatabase _database;
        protected readonly IndexQueryServerSide _query;
        private readonly JsonOperationContext _context;
        private readonly IncludeDocumentsCommand _includeDocumentsCommand;
        private readonly IncludeRevisionsCommand _includeRevisionsCommand;
        private readonly IncludeCompareExchangeValuesCommand _includeCompareExchangeValuesCommand;
        private readonly BlittableJsonTraverser _blittableTraverser;

        private readonly LruDictionary<string, TDocument> _loadedDocumentCache;
        private Dictionary<string, TDocument> _loadedDocumentsByAliasName;
        private HashSet<string> _loadedDocumentIds;

        protected readonly DocumentFields DocumentFields;

        protected readonly DocumentsStorage DocumentsStorage;

        protected readonly FieldsToFetch FieldsToFetch;

        protected readonly QueryTimingsScope RetrieverScope;

        protected readonly SearchEngineType SearchEngineType;


        private QueryTimingsScope _projectionScope;
        private QueryTimingsScope _projectionStorageScope;
        private QueryTimingsScope _functionScope;
        private QueryTimingsScope _loadScope;

        private TimeSeriesRetriever<TDocument> _timeSeriesRetriever;

        protected readonly DocumentsOperationContext DocumentContext;

        protected QueryResultRetrieverBase(
            DocumentDatabase database, IndexQueryServerSide query, QueryTimingsScope queryTimings, SearchEngineType searchEngineType, FieldsToFetch fieldsToFetch, DocumentsStorage documentsStorage,
            JsonOperationContext context, DocumentsOperationContext documentContext, bool reduceResults, IncludeDocumentsCommand includeDocumentsCommand,
            IncludeCompareExchangeValuesCommand includeCompareExchangeValuesCommand, IncludeRevisionsCommand includeRevisionsCommand)
        {
            _database = database;
            _query = query;
            _context = context;
            _includeDocumentsCommand = includeDocumentsCommand;
            _includeRevisionsCommand = includeRevisionsCommand;
            _includeCompareExchangeValuesCommand = includeCompareExchangeValuesCommand;
            SearchEngineType = searchEngineType;

            DocumentContext = documentContext;
            
            ValidateFieldsToFetch(fieldsToFetch);
            FieldsToFetch = fieldsToFetch;

            DocumentsStorage = documentsStorage;

            RetrieverScope = queryTimings?.For(nameof(QueryTimingsScope.Names.Retriever), start: false);

            DocumentFields = query?.DocumentFields ?? DocumentFields.All;

            _blittableTraverser = reduceResults ? BlittableJsonTraverser.FlatMapReduceResults : BlittableJsonTraverser.Default;

            _loadedDocumentCache = typeof(TDocument) == typeof(QueriedDocument) && DocumentContext != null
                ? (LruDictionary<string, TDocument>)(object)(new QueriedDocumentCache(DocumentContext, LoadedDocumentsCacheSize))
                : new LruDictionary<string, TDocument>(LoadedDocumentsCacheSize);
        }



        protected virtual void ValidateFieldsToFetch(FieldsToFetch fieldsToFetch)
        {
            if (fieldsToFetch == null)
                throw new ArgumentNullException(nameof(fieldsToFetch));
        }

        protected void FinishDocumentSetup(TDocument doc, Lucene.Net.Search.ScoreDoc scoreDoc)
        {
            if (doc == null || scoreDoc == null)
                return;

            doc.IndexScore = scoreDoc.Score;
            if (_query?.Distances != null)
            {
                doc.Distance = _query.Distances.Get(scoreDoc.Doc);
            }
        }

        public abstract (TDocument Document, List<TDocument> List) Get(ref RetrieverInput retrieverInput, CancellationToken token);

        public abstract bool TryGetKeyLucene(ref RetrieverInput retrieverInput, out string key);

        public abstract bool TryGetKeyCorax(IndexSearcher searcher, long id, out UnmanagedSpan key);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual TDocument DirectGet(ref RetrieverInput retrieverInput, string id, DocumentFields fields)
        {
            return DirectGetInternal(ref retrieverInput, id, fields);
        }

        public virtual void ClearCache()
        {
            _loadedDocumentCache.Clear();
        }

        private TDocument DirectGetInternal(ref RetrieverInput retrieverInput, string id, DocumentFields fields)
        {
            if (_loadedDocumentCache.TryGetValue(id, out var doc) == false)
            {
                doc = _loadedDocumentCache[id] = DocumentsStorage.Get<TDocument>(DocumentContext, id, fields);
            }

            // When we take document from cache we've to increase the reference counter.
            if (_loadedDocumentCache.IsTrackingSupported)
                _loadedDocumentCache.IncreaseReference(doc);

            return doc;
        }

        protected virtual TDocument LoadDocument(TDocument parentDocument, string id, ref RetrieverInput retrieverInput)
        {
            using var _ = (_loadScope = _loadScope?.Start() ?? _projectionScope?.For(nameof(QueryTimingsScope.Names.Load)));
            if (id == string.Empty) // string.Empty means it's original document.
                return parentDocument;
            
            if (_loadedDocumentCache.TryGetValue(id, out var doc))
            {
                if (_loadedDocumentCache.IsTrackingSupported)
                    _loadedDocumentCache.TrackReferences(parentDocument, doc);

                if (typeof(TDocument) != typeof(Document))
                    return doc;
            }

            doc = DirectGetInternal(ref retrieverInput, id, DocumentFields.All);

            if (doc != null)
            {
                _loadedDocumentCache[id] = doc;

                if (_loadedDocumentCache.IsTrackingSupported)
                    _loadedDocumentCache.TrackReferences(parentDocument, doc);
            }
            
            return doc;
        }

        protected abstract long? GetCounter(string docId, string name);

        protected abstract DynamicJsonValue GetCounterRaw(string docId, string name);

        protected (TDocument Document, List<TDocument> List) GetProjection(ref RetrieverInput retrieverInput, string lowerId, CancellationToken token)
        {
            using (_projectionScope = _projectionScope?.Start() ?? RetrieverScope?.For(nameof(QueryTimingsScope.Names.Projection)))
            {
                TDocument doc = null;

                if (FieldsToFetch.AnyExtractableFromIndex == false)
                {
                    using (_projectionStorageScope = _projectionStorageScope?.Start() ?? _projectionScope?.For(nameof(QueryTimingsScope.Names.Storage)))
                        doc = DirectGet(ref retrieverInput, lowerId, DocumentFields.All);

                    FinishDocumentSetup(doc, retrieverInput.Score);
                    
                    if (doc == null)
                    {
                        if (FieldsToFetch.Projection.MustExtractFromDocument)
                        {
                            if (FieldsToFetch.Projection.MustExtractOrThrow)
                                FieldsToFetch.Projection.ThrowCouldNotExtractProjectionOnDocumentBecauseDocumentDoesNotExistException(lowerId);
                        }

                        return default;
                    }

                    return GetProjectionFromDocumentInternal(doc, ref retrieverInput, FieldsToFetch, _context, token);
                }

                var result = new DynamicJsonValue();

                Dictionary<string, FieldsToFetch.FieldToFetch> fields = null;

               
                if (FieldsToFetch.ExtractAllFromIndex)
                {
                    if (retrieverInput.IsLuceneDocument())
                    {
                        fields = retrieverInput.LuceneDocument!.GetFields()
                            .Where(x => x.Name != Constants.Documents.Indexing.Fields.DocumentIdFieldName
                                        && x.Name != Constants.Documents.Indexing.Fields.SourceDocumentIdFieldName
                                        && x.Name != Constants.Documents.Indexing.Fields.ReduceKeyHashFieldName
                                        && x.Name != Constants.Documents.Indexing.Fields.ReduceKeyValueFieldName
                                        && x.Name != Constants.Documents.Indexing.Fields.ValueFieldName
                                        && FieldUtil.GetRangeTypeFromFieldName(x.Name) == RangeType.None)
                            .Distinct(UniqueFieldNames.Instance)
                            .ToDictionary(x => x.Name, x => new FieldsToFetch.FieldToFetch(x.Name, null, null, x.IsStored, isDocumentId: false, isTimeSeries: false));
                    }
                    else
                    {
                        fields = retrieverInput.KnownFields.Where(i => i.FieldNameAsString.In(
                                Constants.Documents.Indexing.Fields.DocumentIdFieldName,
                                Constants.Documents.Indexing.Fields.SourceDocumentIdFieldName,
                                Constants.Documents.Indexing.Fields.ReduceKeyHashFieldName,
                                Constants.Documents.Indexing.Fields.ReduceKeyValueFieldName,
                                Constants.Documents.Indexing.Fields.ValueFieldName) == false)
                            .ToDictionary(x => x.FieldNameAsString, x => new FieldsToFetch.FieldToFetch(x.FieldNameAsString, null, null, true
                                , isDocumentId: false, isTimeSeries: false));
                    }
                }
                

                if (fields == null)
                {
                    fields = FieldsToFetch.Fields;
                }
                else if (FieldsToFetch.Fields != null && FieldsToFetch.Fields.Count > 0)
                {
                    foreach (var kvp in FieldsToFetch.Fields)
                    {
                        if (fields.ContainsKey(kvp.Key))
                            continue;

                        fields[kvp.Key] = kvp.Value;
                    }
                }
                
                if (fields is not null)
                {
                    foreach (var fieldToFetch in fields?.Values)
                    {
                        if (fieldToFetch.CanExtractFromIndex && // skip `id()` fields here
                            TryExtractValueFromIndex(ref retrieverInput, fieldToFetch, result))
                            continue;
                        
                        if (FieldsToFetch.Projection.MustExtractFromIndex)
                        {
                            if (FieldsToFetch.Projection.MustExtractOrThrow)
                            {
                                if (TryExtractValueFromIndex(ref retrieverInput, fieldToFetch, result)) 
                                    continue; // here we try again, for `id()` fields

                                FieldsToFetch.Projection
                                    .ThrowCouldNotExtractFieldFromIndexBecauseIndexDoesNotContainSuchFieldOrFieldValueIsNotStored(
                                        fieldToFetch.Name
                                            .Value);
                            }

                            continue;
                        }

                        if (doc == null)
                        {
                            using (_projectionStorageScope = _projectionStorageScope?.Start() ?? _projectionScope?.For(nameof(QueryTimingsScope.Names.Storage)))
                                doc = DirectGet(ref retrieverInput, lowerId, DocumentFields.All);

                            if (doc == null)
                            {
                                if (FieldsToFetch.Projection.MustExtractFromDocument)
                                {
                                    if (FieldsToFetch.Projection.MustExtractOrThrow)
                                        FieldsToFetch.Projection.ThrowCouldNotExtractFieldFromDocumentBecauseDocumentDoesNotExistException(lowerId,
                                            fieldToFetch.Name.Value);

                                    break;
                                }

                                // we don't return partial results
                                return default;
                            }
                        }

                        if (TryGetValue(fieldToFetch, doc, ref retrieverInput, FieldsToFetch.IndexFields, FieldsToFetch.AnyDynamicIndexFields, out var key,
                                out var fieldVal, token))
                        {
                            if (FieldsToFetch.SingleBodyOrMethodWithNoAlias)
                            {
                                if (fieldVal is BlittableJsonReaderObject nested)
                                {
                                    if (ReferenceEquals(nested, doc.Data) == false)
                                    {
                                        using (_ = doc)
                                            doc = doc.CloneWith<TDocument>(_context, nested);
                                    }
                                }
                                else if (fieldVal is TDocument dt)
                                {
                                    if (ReferenceEquals(doc, dt) == false)
                                        doc.Dispose();
                                    doc = dt;
                                }
                                else if (fieldVal is Document d)
                                {
                                    using (_ = doc)
                                        doc = d.Clone<TDocument>(_context);
                                }
                                else
                                    ThrowInvalidQueryBodyResponse(fieldVal);
                                FinishDocumentSetup(doc, retrieverInput.Score);
                                return (doc, null);
                            }

                            if (fieldVal is List<object> list)
                                fieldVal = new DynamicJsonArray(list);

                            if (fieldVal is TDocument d2)
                            {
                                if (_loadedDocumentCache.IsTrackingSupported)
                                {
                                    Debug.Assert(typeof(TDocument) == typeof(QueriedDocument), "typeof(TDocument) == typeof(QueriedDocument)");
                                    ((QueriedDocument)(object)doc).LinkReferencedDocument((QueriedDocument)(object)d2);
                                }

                                fieldVal = d2.Data;
                            }

                            result[key] = fieldVal;
                        }
                        else
                        {
                            if (FieldsToFetch.Projection.MustExtractFromDocument)
                            {
                                if (FieldsToFetch.Projection.MustExtractOrThrow)
                                    FieldsToFetch.Projection
                                        .ThrowCouldNotExtractFieldFromDocumentBecauseDocumentDoesNotContainSuchField(lowerId, fieldToFetch.Name.Value);
                            }
                        }
                    }
                }

                if (doc == null)
                {
                    // the fields were projected from the index
                    doc = new TDocument
                    {
                        Id = _context.GetLazyString(lowerId)
                    };
                }

                return (ReturnProjection(result, doc, _context, retrieverInput.Score), null);
            }
        }

        private bool TryExtractValueFromIndex(ref RetrieverInput retrieverInput, FieldsToFetch.FieldToFetch fieldToFetch, DynamicJsonValue result)
        {
            switch (SearchEngineType)
            {
                case SearchEngineType.Corax:
                    if (CoraxTryExtractValueFromIndex(fieldToFetch, ref retrieverInput, result))
                        return true;
                    break;
                case SearchEngineType.Lucene:
                    if (LuceneTryExtractValueFromIndex(fieldToFetch, retrieverInput.LuceneDocument, result, retrieverInput.State))
                        return true;
                    break;
                default:
                    throw new InvalidDataException($"Unknown {nameof(Client.Documents.Indexes.SearchEngineType)}.");
            }

            return false;
        }
        
        public (TDocument Document, List<TDocument> List) GetProjectionFromDocument(TDocument doc, ref RetrieverInput retrieverInput, FieldsToFetch fieldsToFetch, JsonOperationContext context, CancellationToken token)
        {
            using (RetrieverScope?.Start())
            using (_projectionScope = _projectionScope?.Start() ?? RetrieverScope?.For(nameof(QueryTimingsScope.Names.Projection)))
            {
                var documentFromCache = GetOrPersistDocumentInCache();
                return GetProjectionFromDocumentInternal(documentFromCache, ref retrieverInput, fieldsToFetch, context, token);
            }

            unsafe TDocument GetOrPersistDocumentInCache()
            {
                if (_loadedDocumentCache.TryGetValue(doc.LowerId, out var documentFromCache) == false)
                {
                    _loadedDocumentCache[doc.LowerId] = doc;
                    documentFromCache = doc;
                }
                else
                {
                    doc.Dispose(); // suspended.
                    Debug.Assert(documentFromCache.Data.BasePointer != null, "documentFromCache.Data.BasePointer != null");
                }

                if (_loadedDocumentCache.IsTrackingSupported)
                    _loadedDocumentCache.IncreaseReference(documentFromCache);

#if DEBUG
                Debug.Assert(documentFromCache is not QueriedDocument qd || qd.RefCount >= 2, "documentFromCache is not QueriedDocument qd || qd.RefCount >= 2");
#endif

                return documentFromCache;
            }
        }

        private (TDocument Document, List<TDocument> List) GetProjectionFromDocumentInternal(TDocument doc, ref RetrieverInput retrieverInput, FieldsToFetch fieldsToFetch, JsonOperationContext context, CancellationToken token) 
        {
            var result = new DynamicJsonValue();

            foreach (var fieldToFetch in fieldsToFetch.Fields.Values)
            {
                if (TryGetValue(fieldToFetch, doc, ref retrieverInput, fieldsToFetch.IndexFields, fieldsToFetch.AnyDynamicIndexFields, out var key, out var fieldVal, token) == false)
                {
                    if (FieldsToFetch.Projection.MustExtractFromDocument)
                    {
                        if (FieldsToFetch.Projection.MustExtractOrThrow)
                            throw new InvalidQueryException($"Could not extract field '{fieldToFetch.Name.Value}' from document, because document does not contain such a field.");
                    }

                    if (fieldToFetch.QueryField != null && fieldToFetch.QueryField.HasSourceAlias)
                        continue;
                }

                var immediateResult = AddProjectionToResult(doc, retrieverInput.Score, fieldsToFetch, result, key, fieldVal);

                if (immediateResult.Document != null || immediateResult.List != null)
                    return immediateResult;
            }

            return (ReturnProjection(result, doc, context, retrieverInput.Score), null);
        }

        protected (TDocument Document, List<TDocument> List) AddProjectionToResult(TDocument doc, Lucene.Net.Search.ScoreDoc scoreDoc, FieldsToFetch fieldsToFetch, DynamicJsonValue result, string key, object fieldVal)
        {
            if (_query.IsStream &&
                key.StartsWith(Constants.TimeSeries.QueryFunction))
            {
                doc.TimeSeriesStream ??= new TimeSeriesStream();
                var value = (TimeSeriesRetrieverBase.TimeSeriesStreamingRetrieverResult)fieldVal;
                doc.TimeSeriesStream.TimeSeries = value.Stream;
                doc.TimeSeriesStream.Key = key;
                Json.BlittableJsonTextWriterExtensions.MergeMetadata(result, value.Metadata);
                return default;
            }

            if (fieldsToFetch.SingleBodyOrMethodWithNoAlias)
            {
                var r = CreateNewDocument(doc, key, fieldVal);
                FinishDocumentSetup(r.Document, scoreDoc);
                if (r.List == null)
                    return r;
                foreach (var item in r.List)
                {
                    FinishDocumentSetup(item, scoreDoc);
                }
                return r;

            }

            AddProjectionToResult(result, key, fieldVal);
            return default;
        }

        private (TDocument Document, List<TDocument> List) CreateNewDocument(TDocument doc, string key, object fieldVal)
        {
            switch (fieldVal)
            {
                case List<object> list:
                    RuntimeHelpers.EnsureSufficientExecutionStack();
                    using (doc)
                    {
                        var results = new List<TDocument>(list.Count);
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (_loadedDocumentCache.IsTrackingSupported)
                                _loadedDocumentCache.IncreaseReference(doc);

                            var result = CreateNewDocument(doc, key, list[i]);
                            if (result.Document != null)
                            {
                                results.Add(result.Document);
                            }
                            else if (result.List != null)
                            {
                                foreach (var document in result.List)
                                {
                                    results.Add(document);
                                }
                            }
                        }
                        
                        return (null, results);
                    }
                case BlittableJsonReaderObject nested:
                {
                    using (doc)
                    {
                        return (doc.CloneWith<TDocument>(_context, nested), null);
                    }
                }
                case TDocument td:
                    return (td, null);
                case Document d:
                    using (d)
                    {
                        return (d.Clone<TDocument>(_context), null);
                    }
                case TimeSeriesRetrieverBase.TimeSeriesStreamingRetrieverResult ts:
                {
                    var newData = _context.ReadObject(ts.Metadata, "time-series-metadata");
                    var clonedDocument = doc.CloneWith<TDocument>(_context, newData);
                    clonedDocument.TimeSeriesStream = new TimeSeriesStream { TimeSeries = ts.Stream, Key = key };
                    return (clonedDocument, null);
                }
                default:
                    ThrowInvalidQueryBodyResponse(fieldVal);
                    break;
            }

            return default;
        }

        protected static void AddProjectionToResult(DynamicJsonValue result, string key, object fieldVal)
        {
            if (fieldVal is List<object> list)
            {
                var array = new DynamicJsonArray();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is Document d)
                    {
                        d.EnsureDocumentId();

                        array.Add(d.Data);
                    }
                    else
                        array.Add(list[i]);
                }
                fieldVal = array;
            }

            if (fieldVal is Document d2)
            {
                d2.EnsureDocumentId();
                fieldVal = d2.Data;
            }

            result[key] = fieldVal;
        }

        private static void ThrowInvalidQueryBodyResponse(object fieldVal)
        {
            throw new InvalidOperationException("Query returning a single function call result must return an object, but got: " + (fieldVal ?? "null"));
        }

        protected TDocument ReturnProjection(DynamicJsonValue result, TDocument doc, JsonOperationContext context, Lucene.Net.Search.ScoreDoc scoreDoc = null)
        {
            var metadata = Json.BlittableJsonTextWriterExtensions.GetOrCreateMetadata(result);
            metadata[Constants.Documents.Metadata.Projection] = true;

            var newData = context.ReadObject(result, "projection result");
            var sameBlittable = ReferenceEquals(newData, doc.Data);
            if (sameBlittable == false)
            {
                using (_ = doc)
                    doc = doc.CloneWith<TDocument>(context, newData);
            }
            
            if (scoreDoc != null)
                FinishDocumentSetup(doc, scoreDoc);

            return doc;
        }

        private bool LuceneTryExtractValueFromIndex(FieldsToFetch.FieldToFetch fieldToFetch, Lucene.Net.Documents.Document indexDocument, DynamicJsonValue toFill, IState state)
        {
            var name = fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value;

            DynamicJsonArray array = null;
            FieldType fieldType = null;
            var anyExtracted = false;
            foreach (var field in indexDocument.GetFields(fieldToFetch.Name.Value))
            {
                if (fieldType == null)
                    fieldType = GetFieldType(field.Name, indexDocument);

                var fieldValue = ConvertType(_context, field, fieldType, state);

                if (fieldType.IsArray)
                {
                    if (array == null)
                    {
                        array = new DynamicJsonArray();
                        toFill[name] = array;
                    }

                    array.Add(fieldValue);
                    anyExtracted = true;
                    continue;
                }

                toFill[name] = fieldValue;
                anyExtracted = true;
            }

            return anyExtracted;
        }

        private bool CoraxTryExtractValueFromIndex(FieldsToFetch.FieldToFetch fieldToFetch, ref RetrieverInput retrieverInput, DynamicJsonValue toFill)
        {
            // We can always perform projection of ID from Index.
            if (fieldToFetch.CanExtractFromIndex == false && fieldToFetch.IsDocumentId == false)
                return false;


            object value = null;
            if (retrieverInput.KnownFields.TryGetByFieldName(fieldToFetch.Name.Value, out var binding) == false)
            {
                if (TryGetValueFromCoraxIndex(_context, fieldToFetch.Name.Value, Corax.Constants.IndexWriter.DynamicField, ref retrieverInput, out value) == false)
                    return false;
            }
            else if (TryGetValueFromCoraxIndex(_context, fieldToFetch.Name.Value, binding.FieldId, ref retrieverInput, out value) == false)
                return false;
            
            var name = fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value;
            toFill[name] = value;
            
            return true;
        }
        
        private static unsafe bool TryGetValueFromCoraxIndex(JsonOperationContext context, string fieldName, int fieldId, ref RetrieverInput retrieverInput, out object value)
        {
            var fieldReader = fieldId == Corax.Constants.IndexWriter.DynamicField 
                ? retrieverInput.CoraxEntry.GetFieldReaderFor(Encodings.Utf8.GetBytes(fieldName)) : retrieverInput.CoraxEntry.GetFieldReaderFor(fieldId);
            var fieldType = fieldReader.Type;
            value = null;
            switch (fieldType)
            {
                case IndexEntryFieldType.Empty:
                case IndexEntryFieldType.Null:
                value = fieldType == IndexEntryFieldType.Null 
                    ? null :
                    string.Empty;
                break;

            case IndexEntryFieldType.TupleListWithNulls:
            case IndexEntryFieldType.TupleList:
                if (fieldReader.TryReadMany(out var iterator) == false)
                    goto case IndexEntryFieldType.Invalid;
                var tupleList = new DynamicJsonArray();
                while (iterator.ReadNext())
                {
                    if (iterator.IsNull)
                    {
                        tupleList.Add(null);
                    }
                    else if (iterator.IsEmptyCollection)
                    {
                        throw new InvalidDataException("Tuple list cannot contain an empty string (otherwise, where did the numeric came from!)");
                    }
                    else
                    {
                        tupleList.Add(Encodings.Utf8.GetString(iterator.Sequence));
                    }
                }

                value = tupleList;
                break;

            case IndexEntryFieldType.Tuple:
                if (fieldReader.TryReadTuple(out long lVal, out double dVal, out Span<byte> valueInEntry) == false)
                    goto case IndexEntryFieldType.Invalid;

                value = Encodings.Utf8.GetString(valueInEntry);
                
                break;

            case IndexEntryFieldType.SpatialPointList:
                if (fieldReader.TryReadManySpatialPoint(out var spatialIterator) == false)
                    goto case IndexEntryFieldType.Invalid;

                var geoList = new DynamicJsonArray();
                
                while (spatialIterator.ReadNext())
                {
                    geoList.Add(Encodings.Utf8.GetString(spatialIterator.Geohash));
                }

                value = geoList;
                
                break;

            case IndexEntryFieldType.SpatialPoint:
                if (fieldReader.Read(out valueInEntry) == false)
                    goto case IndexEntryFieldType.Invalid;

                value = Encodings.Utf8.GetString(valueInEntry);

                break;

            case IndexEntryFieldType.ListWithEmpty:
            case IndexEntryFieldType.ListWithNulls:
            case IndexEntryFieldType.List:
                if (fieldReader.TryReadMany(out iterator) == false)
                    goto case IndexEntryFieldType.Invalid;
                
                var array = new DynamicJsonArray();
                while (iterator.ReadNext())
                {
                    Debug.Assert((fieldType & IndexEntryFieldType.Tuple) == 0, "(fieldType & IndexEntryFieldType.Tuple) == 0");

                    if (iterator.IsEmptyString || iterator.IsNull)
                    {
                        array.Add(iterator.IsNull ? null : string.Empty);
                    }
                    else
                    {
                        array.Add(Encodings.Utf8.GetString(iterator.Sequence));
                    }
                }

                value = array;
                break;
            case IndexEntryFieldType.RawList:
                if (fieldReader.TryReadMany(out iterator) == false)
                    goto case IndexEntryFieldType.Invalid;
                
                array = new DynamicJsonArray();
                while (iterator.ReadNext())
                {
                    fixed (byte* ptr = &iterator.Sequence.GetPinnableReference())
                        value = new BlittableJsonReaderObject(ptr, iterator.Sequence.Length, context);
                }
                value = array;
                break;
            case IndexEntryFieldType.Raw:
                if (fieldReader.Read(out Span<byte> blittableBinary) == false)
                    goto case IndexEntryFieldType.Invalid;
                
                fixed (byte* ptr = &blittableBinary.GetPinnableReference())
                    value = new BlittableJsonReaderObject(ptr, blittableBinary.Length, context);
                break;
            
            case IndexEntryFieldType.Invalid:
                value = null;
                return false;
            default:
                if (fieldReader.Read(out valueInEntry) == false)
                    goto case IndexEntryFieldType.Invalid;

                value = Encodings.Utf8.GetString(valueInEntry);
                break; 
            }
            
            
            return true;
        }

        private static object ConvertType(JsonOperationContext context, IFieldable field, FieldType fieldType, IState state)
        {
            if (field.IsBinary)
                ThrowBinaryValuesNotSupported();

            var stringValue = field.StringValue(state);

            if (stringValue == null)
                return null;

            if (stringValue == string.Empty)
                return string.Empty;

            if (field.IsTokenized == false)
            {
                // NULL_VALUE and EMPTY_STRING fields aren't tokenized
                // this will prevent converting fields with a "NULL_VALUE" string to null
                switch (stringValue)
                {
                    case Constants.Documents.Indexing.Fields.NullValue:
                        return null;

                    case Constants.Documents.Indexing.Fields.EmptyString:
                        return string.Empty;
                }
            }

            if (fieldType.IsJson == false)
                return stringValue;

            return context.Sync.ReadForMemory(stringValue, field.Name);
        }

        private static void ThrowBinaryValuesNotSupported()
        {
            throw new NotSupportedException("Cannot convert binary values");
        }

        protected bool TryGetValue(FieldsToFetch.FieldToFetch fieldToFetch, TDocument document, ref RetrieverInput retrieverInput, Dictionary<string, IndexField> indexFields, bool? anyDynamicIndexFields, out string key, out object value, CancellationToken token)
        {
            key = fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value;

            if (fieldToFetch.QueryField == null)
            {
                return TryGetFieldValueFromDocument(document, fieldToFetch, out value);
            }

            if (fieldToFetch.QueryField.Function != null)
            {
                var args = new object[fieldToFetch.QueryField.FunctionArgs.Length + 1];
                for (int i = 0; i < fieldToFetch.FunctionArgs.Length; i++)
                {

                    TryGetValue(fieldToFetch.FunctionArgs[i], document, ref retrieverInput, indexFields, anyDynamicIndexFields, out _, out args[i], token);
                    // if (args[i] is QueriedDocument d)
                    //     args[i] = d.Clone<Document>(_context);
                    
                    if (ReferenceEquals(args[i], document))
                    {
                        args[i] = Tuple.Create((Document)document, retrieverInput, indexFields, anyDynamicIndexFields, FieldsToFetch.Projection);
                    }
                }
                value = GetFunctionValue(fieldToFetch, document.Id, args, token);
                return true;
            }


            if (fieldToFetch.QueryField.IsCounter)
            {
                string name;
                string id = document.Id;
                if (fieldToFetch.QueryField.IsParameter)
                {
                    if (_query.QueryParameters == null)
                        throw new InvalidQueryException("The query is parametrized but the actual values of parameters were not provided", _query.Query, null);

                    if (_query.QueryParameters.TryGetMember(fieldToFetch.QueryField.Name, out var nameObject) == false)
                        throw new InvalidQueryException($"Value of parameter '{fieldToFetch.QueryField.Name}' was not provided", _query.Query, _query.QueryParameters);

                    name = nameObject.ToString();
                    key = fieldToFetch.QueryField.Alias ?? name;
                }
                else
                {
                    name = fieldToFetch.Name.Value;
                }

                if (fieldToFetch.QueryField.SourceAlias != null
                    && BlittableJsonTraverser.Default.TryRead(document.Data, fieldToFetch.QueryField.SourceAlias, out var sourceId, out _))
                {
                    id = sourceId.ToString();
                }

                if (fieldToFetch.QueryField.FunctionArgs != null)
                {
                    value = GetCounterRaw(id, name);
                }
                else
                {
                    value = GetCounter(id, name);
                }

                return true;
            }

            if (fieldToFetch.QueryField.ValueTokenType != null)
            {
                var val = fieldToFetch.QueryField.Value;
                if (fieldToFetch.QueryField.ValueTokenType.Value == ValueTokenType.Parameter)
                {
                    if (_query == null)
                    {
                        value = null;
                        return false; // only happens for debug endpoints and more like this
                    }
                    _query.QueryParameters.TryGet((string)val, out val);
                }
                value = val;
                return true;
            }

            if (fieldToFetch.QueryField.HasSourceAlias == false)
            {
                return TryGetFieldValueFromDocument(document, fieldToFetch, out value);
            }
            if (_loadedDocumentIds == null)
            {
                _loadedDocumentIds = new HashSet<string>();
                _loadedDocumentsByAliasName = new Dictionary<string, TDocument>();
            }

            if (_loadedDocumentCache.IsTrackingSupported && _loadedDocumentIds != null)
            {
                foreach (var loadedDocId in _loadedDocumentIds)
                {
                    if (string.IsNullOrEmpty(loadedDocId))
                        continue;
                    
                    if (_loadedDocumentCache.TryGetValue(loadedDocId.ToLowerInvariant(), out var doc))
                        doc?.Dispose();
                }
            }
            
            _loadedDocumentIds.Clear();

            //_loadedDocuments.Clear(); - explicitly not clearing this, we want to cache this for the duration of the query
            

            if (fieldToFetch.QueryField.SourceAlias != null)
            {
                if (fieldToFetch.QueryField.IsQuoted)
                {
                    _loadedDocumentIds.Add(fieldToFetch.QueryField.SourceAlias);
                }
                else if (fieldToFetch.QueryField.IsParameter)
                {
                    if (_query.QueryParameters == null)
                        throw new InvalidQueryException("The query is parametrized but the actual values of parameters were not provided", _query.Query, (BlittableJsonReaderObject)null);

                    if (_query.QueryParameters.TryGetMember(fieldToFetch.QueryField.SourceAlias, out var id) == false)
                        throw new InvalidQueryException($"Value of parameter '{fieldToFetch.QueryField.SourceAlias}' was not provided", _query.Query, _query.QueryParameters);

                    _loadedDocumentIds.Add(id.ToString());
                }
                else if (fieldToFetch.QueryField.LoadFromAlias != null)
                {
                    if (_loadedDocumentsByAliasName.TryGetValue(fieldToFetch.QueryField.LoadFromAlias, out var loadedDoc))
                    {
                        IncludeUtil.GetDocIdFromInclude(loadedDoc.Data, fieldToFetch.QueryField.SourceAlias, _loadedDocumentIds, _database.IdentityPartsSeparator);
                    }
                }
                else if (fieldToFetch.CanExtractFromIndex)
                {
                    object fieldValue = null;
                    switch (SearchEngineType)
                    {
                        case SearchEngineType.Lucene:
                            var fields = retrieverInput.LuceneDocument.GetFields(fieldToFetch.QueryField.SourceAlias);
                            if (fields != null && fields.Length > 0)
                            {
                                foreach (var field in fields)
                                {
                                    if (field == null)
                                        continue;

                                    fieldValue = ConvertType(_context, field, GetFieldType(field.Name, retrieverInput.LuceneDocument), retrieverInput.State);
                                    _loadedDocumentIds.Add(fieldValue.ToString());
                                }
                            }
                            break;
                        case SearchEngineType.Corax:
                            if (indexFields.TryGetValue(fieldToFetch.QueryField.SourceAlias, out var fieldDefinition) == false ||
                                TryGetValueFromCoraxIndex(_context, fieldDefinition.Name ?? fieldDefinition.OriginalName, fieldDefinition.Id, ref retrieverInput, out fieldValue) == false)
                            {
                                throw new InvalidDataException($"Field {fieldToFetch.QueryField.SourceAlias} not found in index");
                            }
                            
                            if (fieldValue is IEnumerable<object> enumerable)
                            {
                                foreach (var item in enumerable)
                                    if (item != null)
                                        _loadedDocumentIds.Add(item.ToString());
                            }
                            else
                                _loadedDocumentIds.Add(fieldValue?.ToString());

                            break;
                        case SearchEngineType.None:
                            throw new InvalidDataException($"Unknown {nameof(Client.Documents.Indexes.SearchEngineType)}.");
                    }
                }
                else
                {
                    IncludeUtil.GetDocIdFromInclude(document.Data, fieldToFetch.QueryField.SourceAlias, _loadedDocumentIds, _database.IdentityPartsSeparator);
                }
            }
            else
            {
                _loadedDocumentIds.Add(document.Id ?? string.Empty); // null source alias is the root doc
                _loadedDocumentsByAliasName.Clear();
            }

            if (_loadedDocumentIds.Count == 0)
            {
                if (fieldToFetch.QueryField.SourceIsArray)
                {
                    value = new List<object>();
                    return true;
                }
                value = null;
                return false;
            }

            var buffer = new List<object>();

            foreach (var docId in _loadedDocumentIds)
            {
                if (docId == null)
                    continue;

                var doc = LoadDocument(document, docId.ToLowerInvariant(), ref retrieverInput);
                if (doc == null)
                    continue;

                if (fieldToFetch.QueryField.Alias != null)
                {
                    _loadedDocumentsByAliasName[fieldToFetch.QueryField.Alias] = doc;
                }

                if (string.IsNullOrEmpty(fieldToFetch.Name.Value)) // we need the whole document here
                {
                    buffer.Add(doc);
                    continue;
                }
                if (TryGetFieldValueFromDocument(doc, fieldToFetch, out var val))
                {
                    if (val is string == false && val is LazyStringValue == false && val is System.Collections.IEnumerable items)
                    {
                        // we flatten arrays in projections
                        foreach (var item in items)
                        {
                            buffer.Add(item);
                        }

                        fieldToFetch.QueryField.SourceIsArray = true;
                    }
                    else
                    {
                        buffer.Add(val);
                    }
                }
            }

            if (fieldToFetch.QueryField.SourceIsArray)
            {
                value = buffer;
                return true;
            }
            if (buffer.Count > 0)
            {
                if (buffer.Count > 1)
                {
                    ThrowOnlyArrayFieldCanHaveMultipleValues(fieldToFetch);
                }
                value = buffer[0];
                return true;
            }
            value = null;
            return false;
        }

        protected object GetFunctionValue(FieldsToFetch.FieldToFetch fieldToFetch, string documentId, object[] args, CancellationToken token)
        {
            using (_functionScope = _functionScope?.Start() ?? _projectionScope?.For(nameof(QueryTimingsScope.Names.JavaScript)))
            {
                args[args.Length - 1] = _query.QueryParameters;
                var value = InvokeFunction(
                    fieldToFetch.QueryField.Name,
                    _query.Metadata.Query,
                    documentId,
                    args,
                    _functionScope,
                    token);

                return value;
            }
        }

        private class QueryKey : ScriptRunnerCache.Key
        {
            private readonly Dictionary<string, DeclaredFunction> _functions;

            private bool Equals(QueryKey other)
            {
                if (_functions?.Count != other._functions?.Count)
                    return false;

                foreach (var function in _functions ?? Enumerable.Empty<KeyValuePair<string, DeclaredFunction>>())
                {
                    if (other._functions != null &&
                        (other._functions.TryGetValue(function.Key, out var otherVal) == false ||
                         function.Value.FunctionText != otherVal.FunctionText))
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != GetType())
                    return false;
                return Equals((QueryKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var function in _functions ?? Enumerable.Empty<KeyValuePair<string, DeclaredFunction>>())
                    {
                        hashCode = (hashCode * 397) ^ (function.Value.FunctionText.GetHashCode());
                    }

                    return hashCode;
                }
            }

            public QueryKey(Dictionary<string, DeclaredFunction> functions)
            {
                _functions = functions;
            }

            public override void GenerateScript(ScriptRunner runner)
            {
                foreach (var kvp in _functions ?? Enumerable.Empty<KeyValuePair<string, DeclaredFunction>>())
                {
                    if (kvp.Value.Type == DeclaredFunction.FunctionType.TimeSeries)
                    {
                        runner.AddTimeSeriesDeclaration(kvp.Value);
                        continue;
                    }

                    if (kvp.Value.Type != DeclaredFunction.FunctionType.JavaScript)
                        continue;

                    runner.AddScript(kvp.Value.FunctionText);
                }
            }
        }

        private object InvokeFunction(string methodName, Query query, string documentId, object[] args, QueryTimingsScope timings, CancellationToken token)
        {
            if (TryGetTimeSeriesFunction(methodName, query, out var func))
            {
                _timeSeriesRetriever ??= new TimeSeriesRetriever<TDocument>(_includeDocumentsCommand.Context, _query.QueryParameters, _loadedDocumentCache, token);
                var result = _timeSeriesRetriever.InvokeTimeSeriesFunction(func, documentId, args, out var type);
                if (_query.IsStream)
                    return _timeSeriesRetriever.PrepareForStreaming(result, FieldsToFetch.SingleBodyOrMethodWithNoAlias, _query.AddTimeSeriesNames);
                return _timeSeriesRetriever.MaterializeResults(result, type, FieldsToFetch.SingleBodyOrMethodWithNoAlias, _query.AddTimeSeriesNames);
            }

            var key = new QueryKey(query.DeclaredFunctions);
            using (_database.Scripts.GetScriptRunner(key, readOnly: true, patchRun: out var run))
            using (var result = run.Run(_context, _context as DocumentsOperationContext, methodName, args, timings, token))
            {
                _includeDocumentsCommand?.AddRange(run.Includes, documentId);
                _includeRevisionsCommand?.AddRange(run.IncludeRevisionsChangeVectors);
                _includeRevisionsCommand?.AddRevisionByDateTimeBefore(run.IncludeRevisionByDateTimeBefore, documentId);
                _includeCompareExchangeValuesCommand?.AddRange(run.CompareExchangeValueIncludes);


                if (result.IsNull)
                    return null;

                return run.Translate(result, _context, QueryResultModifier.Instance);
            }
        }

        private static bool TryGetTimeSeriesFunction(string methodName, Query query, out DeclaredFunction func)
        {
            func = default;

            return query.DeclaredFunctions != null &&
                   query.DeclaredFunctions.TryGetValue(methodName, out func) &&
                   func.Type == DeclaredFunction.FunctionType.TimeSeries;
        }

        private bool TryGetFieldValueFromDocument(TDocument document, FieldsToFetch.FieldToFetch field, out object value)
        {
            if (field.IsDocumentId)
            {
                value = GetIdFromDocument(document);
            }
            else if (field.IsCompositeField == false)
            {
                if (BlittableJsonTraverserHelper.TryRead(_blittableTraverser, document, field.Name, out value) == false)
                {
                    if (field.ProjectedName == null)
                        return false;
                    if (BlittableJsonTraverserHelper.TryRead(_blittableTraverser, document, field.ProjectedName, out value) == false)
                        return false;
                }
            }
            else
            {
                var component = new DynamicJsonValue();

                foreach (var componentField in field.Components)
                {
                    if (BlittableJsonTraverserHelper.TryRead(_blittableTraverser, document, componentField, out var componentValue))
                        component[componentField] = componentValue;
                }

                value = component;
            }
            return true;
        }

        private static string GetIdFromDocument(TDocument document)
        {
            if (document.Id != null)
            {
                return document.Id;
            }

            if (document.Data == null)
            {
                return null;
            }

            if (document.Data.TryGet(Constants.Documents.Metadata.IdProperty, out string docId))
            {
                return docId;
            }

            if (document.TryGetMetadata(out var md) &&
                (md.TryGet(Constants.Documents.Metadata.Id, out docId) ||
                 md.TryGet(Constants.Documents.Metadata.IdProperty, out docId)))
            {
                return docId;
            }

            return null;
        }

        private static void ThrowOnlyArrayFieldCanHaveMultipleValues(FieldsToFetch.FieldToFetch fieldToFetch)
        {
            throw new NotSupportedException(
                $"Attempted to read multiple values in field {fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value}, but it isn't an array and should have only a single value, did you forget '[]' ?");
        }

        private class UniqueFieldNames : IEqualityComparer<IFieldable>
        {
            public static readonly UniqueFieldNames Instance = new UniqueFieldNames();

            public bool Equals(IFieldable x, IFieldable y)
            {
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(IFieldable obj)
            {
                return obj.Name.GetHashCode();
            }
        }

        private class QueryResultModifier : JsBlittableBridge.IResultModifier
        {
            public static readonly QueryResultModifier Instance = new QueryResultModifier();

            private QueryResultModifier()
            {
            }

            public void Modify(ObjectInstance json)
            {
                ObjectInstance metadata;
                var value = json.Get(Constants.Documents.Metadata.Key);
                if (value.Type == Types.Object)
                    metadata = value.AsObject();
                else
                {
                    metadata = json.Engine.Object.Construct(Array.Empty<JsValue>());
                    json.Set(Constants.Documents.Metadata.Key, metadata, false);
                }

                metadata.Set(Constants.Documents.Metadata.Projection, JsBoolean.True, false);
            }
        }
    }
}
