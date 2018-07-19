using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Onnea.Domain;
using System;
using System.Threading.Tasks;

namespace Onnea.DbInterfaces
{
    class CosmosDbInterface
    {
        private readonly DocumentClient _client;

        public CosmosDbInterface()
        {
            _client = new DocumentClient( new Uri( Definitions.CosmosDbEndpoint ), Definitions.CosmosDbKey );
        }

        public void Init()
        {
            var db        = _client.CreateDatabaseIfNotExistsAsync( 
                                    new Database { Id = Definitions.CosmosDbName } ).Result;
            var companies = _client.CreateDocumentCollectionIfNotExistsAsync( 
                                    UriFactory.CreateDatabaseUri( Definitions.CosmosDbName ), 
                                    new DocumentCollection { Id = Definitions.DbCompaniesCollectionName } ).Result;
            return;
        }

        public async Task<ResourceResponse<Document>> UpsertCompanyInfo( CompanyInfo ci )
        {
            return await _client.UpsertDocumentAsync( UriFactory.CreateDocumentCollectionUri(
                                    Definitions.CosmosDbName, Definitions.DbCompaniesCollectionName ), ci,
                                    new RequestOptions() );
        }
    }
}
