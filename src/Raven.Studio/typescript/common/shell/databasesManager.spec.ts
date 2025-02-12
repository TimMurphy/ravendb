/**
 * @jest-environment jsdom
 */

import databasesManager from "common/shell/databasesManager";
import endpointConstants from "endpoints";
import shardedDatabase from "models/resources/shardedDatabase";
import shard from "models/resources/shard";
import { ajaxMock } from "test/mocks";
import nonShardedDatabase from "models/resources/nonShardedDatabase";
import { DatabasesStubs } from "test/stubs/DatabasesStubs";

function mockResponse(dto: StudioDatabasesResponse) {
    ajaxMock.mockImplementation((args: JQueryAjaxSettings) => {
        if (args.url === endpointConstants.global.studioDatabases.studioTasksDatabases) {
            return $.Deferred<StudioDatabasesResponse>().resolve(dto);
        }
    });
}

describe("databasesManager", () => {
    
    beforeEach(() => {
        jest.clearAllMocks();
    });
    
    it("can handle non-sharded database", async () => {
        const response: StudioDatabasesResponse = { 
            Databases: [ DatabasesStubs.nonShardedSingleNodeDatabaseDto() ],
            Orchestrators: []
        }
        
        mockResponse(response);

        const manager = new databasesManager();
        await manager.init();

        const dbs = manager.databases();
        expect(dbs)
            .toHaveLength(1);

        const firstDb = dbs[0];
        expect(firstDb)
            .toBeInstanceOf(nonShardedDatabase);
        expect(firstDb.name)
            .toEqual(response.Databases[0].Name);
    })
    
    it("can handle sharded database", async () => {
        const response: StudioDatabasesResponse = {
            Databases: [DatabasesStubs.shardedDatabaseDto()],
            Orchestrators: []
        }

        mockResponse(response);
        
        const manager = new databasesManager();
        await manager.init();
        
        const dbs = manager.databases();
        
        expect(dbs)
            .toHaveLength(1);
        
        const expectedShardedDatabaseGroup = (response.Databases[0].Name.split("$")[0]);
        
        const firstDb = dbs[0];
        expect(firstDb)
            .toBeInstanceOf(shardedDatabase);
        expect(firstDb.name)
            .toEqual(expectedShardedDatabaseGroup);
        
        const sharded = firstDb as shardedDatabase;
        const shards = sharded.shards();
        expect(shards)
            .toHaveLength(3);
        
        expect(shards[0].name)
            .toEqual(response.Databases[0].Name + "$0");
        expect(shards[0])
            .toBeInstanceOf(shard);

        expect(shards[1].name)
            .toEqual(response.Databases[0].Name + "$1");
        expect(shards[1])
            .toBeInstanceOf(shard);
    });
    
    it("can get single shard by name", async () => {
        const response: StudioDatabasesResponse = {
            Databases: [DatabasesStubs.shardedDatabaseDto()],
            Orchestrators: []
        }

        mockResponse(response);

        const manager = new databasesManager();
        await manager.init();
        
        const firstShardName = response.Databases[0].Name;
        
        const singleShard = manager.getDatabaseByName(firstShardName + "$0") as shard;
        
        expect(singleShard)
            .not.toBeNull();
        expect(singleShard)
            .toBeInstanceOf(shard);
        
        const shardGroup = singleShard.parent;
        expect(shardGroup)
            .not.toBeNull();
        expect(shardGroup)
            .toBeInstanceOf(shardedDatabase);
        expect(shardGroup.shards())
            .toHaveLength(3);
    });

    it("can get sharded database by name", async () => {
        const response: StudioDatabasesResponse = {
            Databases: [DatabasesStubs.shardedDatabaseDto()],
            Orchestrators: []
        }

        mockResponse(response);

        const manager = new databasesManager();
        await manager.init();

        const shardGroupName = response.Databases[0].Name.split("$")[0];

        const shard = manager.getDatabaseByName(shardGroupName) as shardedDatabase;

        expect(shard)
            .not.toBeNull();
        expect(shard)
            .toBeInstanceOf(shardedDatabase);
        expect(shard.shards())
            .toHaveLength(3);
    });
    
    it("can update manager after db was deleted", async () => {
        const response: StudioDatabasesResponse = {
            Databases: [DatabasesStubs.shardedDatabaseDto()],
            Orchestrators: []
        }

        mockResponse(response);

        const manager = new databasesManager();
        await manager.init();

        mockResponse({
            Databases: [],
            Orchestrators: []
        });
        
        await manager.refreshDatabases();
        
        expect(manager.databases())
            .toHaveLength(0);
    });

    it("can update manager after single shard was deleted", async () => {
        const response: StudioDatabasesResponse = {
            Databases: [DatabasesStubs.shardedDatabaseDto()], 
            Orchestrators: []
        }

        mockResponse(response);

        const manager = new databasesManager();
        await manager.init();

        delete response.Databases[0].Sharding.Shards[2];

        mockResponse(response);

        await manager.refreshDatabases();

        expect(manager.databases())
            .toHaveLength(1);
        
        const db1 = manager.getDatabaseByName("sharded") as shardedDatabase;
        expect(db1.shards())
            .toHaveLength(2);
    })
})


