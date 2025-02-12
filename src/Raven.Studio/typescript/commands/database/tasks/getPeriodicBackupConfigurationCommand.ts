import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class getPeriodicBackupConfigurationCommand extends commandBase {
    private readonly databaseName: string;
    private readonly taskId: number;

    constructor( db: database | string,  taskId: number) {
        super();
        this.databaseName = (_.isString(db) ? db : db.name);
        this.taskId = taskId;
    }
 
    execute(): JQueryPromise<Raven.Client.Documents.Operations.Backups.PeriodicBackupConfiguration> {
        const url = endpoints.global.backupDatabase.periodicBackup +
            this.urlEncodeArgs({ name: this.databaseName, taskId: this.taskId });

        return this.query<Raven.Client.Documents.Operations.Backups.PeriodicBackupConfiguration>(url, null)
            .fail((response: JQueryXHR) => {
                this.reportError(`Failed to get periodic backup configuration for task: ${this.taskId}`,
                    response.responseText, response.statusText);
            });
    }
}

export = getPeriodicBackupConfigurationCommand; 

