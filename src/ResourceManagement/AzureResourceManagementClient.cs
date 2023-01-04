using System;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Azure.Identity;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Monitor;
using Azure.ResourceManager.Monitor.Models;

using System.Text;

namespace Azure.ApiWrapper.ResourceManagement;

public class AzureResourceManagementClient
{
    ArmClient armClient;
    TokenCredential credential;
    List<string> eventLog;
    HttpClient client;

    public AzureResourceManagementClient (
        List<string> _eventLog = null,
        DefaultAzureCredential _credential = null
    )
    {
        if(_eventLog != null) { eventLog = _eventLog; } 
            else { eventLog = new List<string>(); }
        if(_credential != null) { credential = _credential; } 
            else { credential = new DefaultAzureCredential(); }
        
        armClient = new ArmClient(credential); 

        client = new HttpClient();
    }

    public List<ResourceGroupResource> GetResourceGroups()
    {
        var rgList = new List<ResourceGroupResource>();
        foreach(var s in armClient.GetSubscriptions())
        {
            rgList.AddRange(s.GetResourceGroups());
        }
        return rgList;
    }

    public ResourceGroupResource GetResourceGroupByName(string subscriptionId, string rgDisplayName)
    {
        return armClient.GetSubscriptionResource(new ResourceIdentifier("/subscriptions/"+ subscriptionId)).GetResourceGroup(rgDisplayName).Value;
    }

    public async Task SetResourceGroupSox(string subscriptionId, ResourceGroupResource rg, string actionGroupId)
    {
        eventLog.Add("Setting Lock deletion Alert");
        await SetResourceGroupLockDelAlert(subscriptionId, rg, actionGroupId);
        eventLog.Add("Setting Sox Tags");
        SetResourceGroupTagsSox(rg);
        eventLog.Add("Setting Read Only Lock");
        await SetResourceGroupLockReadOnly(subscriptionId, rg, "AutoSoxLock");
    }

    public async Task SetResourceGroupLockDelAlert(string subscriptionId, ResourceGroupResource rg, string actionGroupId)
    {
        var resourceGroupName = rg.Data.Name;
        var ruleName = $"AutoLockMonitoring{resourceGroupName}";

        // var actionGroupId = @"/subscriptions/2c093e17-a8ee-4f4c-a025-4dc61ff3fdfd/resourcegroups/testresgroup2/providers/microsoft.insights/actiongroups/testag";

        var url = $@"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Insights/activityLogAlerts/{ruleName}?api-version=2020-10-01";

        // var content = 
        //     new JObject(
        //         new JProperty("properties", new JObject(
        //             new JProperty("description", "Auto Generated Alert Rule Monitoring Lock Deletion"),
        //             new JProperty("actions", new JObject(
        //                 new JProperty("actionGroups", new JArray(
        //                     new JObject(
        //                         new JProperty("actionGroupId", actionGroupId),
        //                         new JProperty("webhookProperties", new JObject(
        //                             new JProperty("sampleWebhookProperty", "SamplePropertyValue")
        //                         ))
        //                     )
        //                 ))
        //             )),
        //             new JProperty("condition", new JObject(
        //                 new JProperty("allOf", new JArray(
        //                     new JObject(
        //                         new JProperty("equals", "Administrative"),
        //                         new JProperty("field", "category")
        //                     ),
        //                     new JObject(
        //                         new JProperty("equals", "Microsoft.Authorization/locks/delete"),
        //                         new JProperty("field", "operationName")
        //                     )
        //                 ))
        //             )),
        //             new JProperty("enabled", true),
        //             new JProperty("location", "Global"),
        //             new JProperty("scopes", $@"/subscriptions/{subscriptionId}"),
        //             new JProperty("tags", new JObject())
        //         ))
        //     );

        // var s = new JsonSerializerSettings { DateFormatHandling = DateFormatHandling.MicrosoftDateFormat };

        var jst = $"{{\"location\": \"Global\",\"tags\": {{}},\"properties\": {{\"scopes\": [\"/subscriptions/{subscriptionId}\"],\"condition\": {{\"allOf\": [{{\"field\": \"category\",\"equals\": \"Administrative\"}},{{\"field\": \"operationName\",\"equals\": \"Microsoft.Authorization/locks/delete\"}}]}},\"actions\": {{\"actionGroups\": [{{\"actionGroupId\": \"{actionGroupId}\",\"webhookProperties\": {{\"sampleWebhookProperty\": \"SamplePropertyValue\"}}}}]}},\"enabled\": true,\"description\": \"Description of sample Activity Log Alert rule.\"}}}}";
        

        // var response = await AzureRestPut(url, JsonConvert.SerializeObject(content, s));
        var response = await AzureRestPut(url, jst);

        if (response.IsSuccessStatusCode == false)
        {
            // throw new Exception("SetResourceGroupLockReadOnly REST call failed:\n" + response.ToString() + "\n\n" + content.ToString());
            throw new Exception("SetResourceGroupLockReadOnly REST call failed:\n" + response.ToString() + "\n\n" + jst);
        }
    }

    public void SetResourceGroupTag(ResourceGroupResource rg, string key, string value)
    {
        rg.AddTag(key, value);
    }

    public void SetResourceGroupTagsSox(ResourceGroupResource rg)
    {
        SetResourceGroupTag(rg, "Example Sox Tag", "Example Value");
    }

    public async Task SetResourceGroupLockReadOnly(string subscriptionId, ResourceGroupResource rg, string lockName)
    {
        var resourceGroupName = rg.Data.Name;
        var url = $@"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.Authorization/locks/{lockName}?api-version=2016-09-01";

        var content = 
            new JObject(
                new JProperty("properties",
                    new JObject(new JProperty("level", "ReadOnly"))
                )
            );

        var response = await AzureRestPut(url, content.ToString());

        if (response.IsSuccessStatusCode == false)
        {
            throw new Exception("SetResourceGroupLockReadOnly REST call failed:\n\t" + response.ToString());
        }
    }

    public Task<HttpResponseMessage> AzureRestPut(string restUrl, string content)
    {
        SetHttpHeaders();
        return client.PutAsync(
            restUrl, 
            new StringContent(content, Encoding.UTF8, "application/json")
        );
    }

    public Task<HttpResponseMessage> AzureRestGet(string restUrl)
    {
        SetHttpHeaders();
        return client.GetAsync(
            restUrl
        );
    }

    public string GetTokenString()
    {
        // Create permission scopes to pass to manual token request
        var scopesDiagnostic =  new[] {
            "https://management.azure.com/.default"
        };

        // Get token object from credential and extract token string
        var token = credential.GetToken(
            new Azure.Core.TokenRequestContext(scopesDiagnostic),
            System.Threading.CancellationToken.None
        );
        return token.Token;
    }

    public void SetHttpHeaders()
    {
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + GetTokenString());
        client.DefaultRequestHeaders.Add("Host", "management.azure.com");
    }
}
