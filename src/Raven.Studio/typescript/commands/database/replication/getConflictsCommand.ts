import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class getConflictsCommand extends commandBase {

    constructor(private ownerDb: database, private start: number, private pageSize: number, private continuationToken?: string) {
        super();
    }

    execute(): JQueryPromise<pagedResultWithTokenAndSkippedResults<replicationConflictListItemDto>> {
        const args = this.getArgsToUse();
        const url = endpoints.databases.replication.replicationConflicts + this.urlEncodeArgs(args);

        const transformer = (result: resultsWithCountScannedResultsAndToken<replicationConflictListItemDto>): pagedResultWithTokenAndSkippedResults<replicationConflictListItemDto> => {
            return {
                items: result.Results,
                totalResultCount: result.TotalResults,
                continuationToken: result.ContinuationToken,
                scannedResults: result.ScannedResults
            };
        }

        return this.query<pagedResult<replicationConflictListItemDto>>(url, null, this.ownerDb, transformer);
    }

    private getArgsToUse() {
        if (this.continuationToken) {
            return {
                continuationToken: this.continuationToken
            };
        }

        return {
            start: this.start,
            pageSize: this.pageSize
        };
    }
}

export = getConflictsCommand;
