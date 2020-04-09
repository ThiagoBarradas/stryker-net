﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Stryker.Core.Logging;
using Stryker.Core.Options;
using Stryker.Core.Testing;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Stryker.Core.Clients
{
    public interface IDashboardClient
    {
        Task<string> PublishReport(string json, string version);
    }

    public class DashboardClient : IDashboardClient
    {
        private readonly StrykerOptions _options;
        private readonly IChalk _chalk;

        public DashboardClient(StrykerOptions options, IChalk chalk)
        {
            _options = options;
            _chalk = chalk;
        }

        public async Task<string> PublishReport(string json, string version)
        {
            var url = new Uri($"{_options.DashboardUrl}/api/reports/{_options.ProjectName}/{version}");

            if (_options.ModuleName != null)
            {
                url = new Uri(url, $"?module={_options.ModuleName}");
            }

            using (var httpClient = new HttpClient())
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                requestMessage.Headers.Add("X-Api-Key", _options.DashboardApiKey);

                var response = await httpClient.SendAsync(requestMessage);

                if(response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();

                    return JsonConvert.DeserializeAnonymousType(jsonResponse, new { Href = "" }).Href;
                } else
                {
                    var logger = ApplicationLogging.LoggerFactory.CreateLogger<DashboardClient>();

                    logger.LogError("Dashboard upload failed with statuscode {0} and message: {1}", response.StatusCode.ToString(), await response.Content.ReadAsStringAsync());
                    return null;
                }
            }
        }
    }
}
