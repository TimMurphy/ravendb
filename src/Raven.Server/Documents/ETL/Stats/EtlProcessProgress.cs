using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Documents.Operations.ETL.Queue;

namespace Raven.Server.Documents.ETL.Stats
{
    public sealed class EtlTaskProgress
    {
        public string TaskName { get; set; }

        public EtlType EtlType { get; set; }
        
        public QueueBrokerType? QueueBrokerType { get; set; }

        public EtlProcessProgress[] ProcessesProgress { get; set; }
    }

    public sealed class EtlProcessProgress
    {
        public string TransformationName { get; set; }
        
        public string TransactionalId { get; set; }

        public bool Completed { get; set; }

        public bool Disabled { get; set; }

        public double AverageProcessedPerSecond { get; set; }

        public long NumberOfDocumentsToProcess { get; set; }

        public long TotalNumberOfDocuments { get; set; }

        public long NumberOfDocumentTombstonesToProcess { get; set; }

        public long TotalNumberOfDocumentTombstones { get; set; }

        public long NumberOfCounterGroupsToProcess { get; set; }

        public long TotalNumberOfCounterGroups { get; set; }
        
        public long NumberOfTimeSeriesSegmentsToProcess { get; set; }
        
        public long TotalNumberOfTimeSeriesSegments { get; set; }
        
        public long NumberOfTimeSeriesDeletedRangesToProcess { get; set; }
        
        public long TotalNumberOfTimeSeriesDeletedRanges { get; set; }
    }
}
