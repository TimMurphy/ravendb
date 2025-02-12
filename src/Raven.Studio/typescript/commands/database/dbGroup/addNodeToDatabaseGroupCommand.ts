import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class addNodeToDatabaseGroupCommand extends commandBase {

    private databaseName: string;

    private nodeTagToAdd: string;

    private mentorNode: string = undefined;

    constructor(databaseName: string, nodeTagToAdd: string, mentorNode: string = undefined) {
        super();
        this.mentorNode = mentorNode;
        this.nodeTagToAdd = nodeTagToAdd;
        this.databaseName = databaseName;
    }

    execute(): JQueryPromise<Raven.Client.ServerWide.Operations.DatabasePutResult> {
        const args = {
            name: this.databaseName,
            node: this.nodeTagToAdd,
            mentor: this.mentorNode
        };
        const url = endpoints.global.adminDatabases.adminDatabasesNode + this.urlEncodeArgs(args);

        return this.put<Raven.Client.ServerWide.Operations.DatabasePutResult>(url, null)
            .done(() => this.reportSuccess("Node " + this.nodeTagToAdd + " was added"))
            .fail((response: JQueryXHR) => this.reportError("Failed to add node to database group", response.responseText, response.statusText));
    }
}

export = addNodeToDatabaseGroupCommand;
