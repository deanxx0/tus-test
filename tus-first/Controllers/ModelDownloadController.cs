using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace tus_first.Controllers
{
    [Route("files/modelfiles")]
    public class ModelFileDownloadController : ControllerBase
    {
        string _outputPath;
        string _apiAddress;
        public ModelFileDownloadController(IConfiguration configuration)
        {
            _outputPath = configuration.GetValue<string>("TRAIN_OUTPUT_DIR");
            _apiAddress = configuration.GetValue<string>("API_ADDRESS");
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Download(string id, string accessToken)
        {
            //confirm
            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri($"{_apiAddress}");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var response = client.GetAsync("user/confirm").Result;
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new Exception("invalid token");
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
            }

            //back-trainid => trainserver-trainid
            string trainserverId = "";

            //back-trainName
            string trainName = "";
            try
            {
                using (var client = new HttpClient())
                {
                    client.BaseAddress = new Uri($"{_apiAddress}");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var response = client.GetAsync($"train/{id}").Result;
                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                        throw new Exception();

                    var doc = JsonSerializer.Deserialize<JsonDocument>(response.Content.ReadAsStringAsync().Result);
                    trainName = doc.RootElement.GetProperty("result").GetProperty("name").GetString();
                    trainserverId = doc.RootElement.GetProperty("result").GetProperty("serverTrainId").GetString();
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
            }

            //backend-trainid =>  serverId

            var path = Path.Combine(_outputPath, trainserverId, "models", "model.dat");
            if (System.IO.File.Exists(path))
            {
                byte[] bytes;
                using (FileStream file = new FileStream(path: path, mode: FileMode.Open))
                { 
                    try
                    {
                        bytes = new byte[file.Length];
                        await file.ReadAsync(bytes);

                        Response.Headers.Add("file-name", $"{trainName}.dat");
                        return File(bytes, "application/octet-stream"); 
                    } 
                    catch (Exception ex)
                    {
                        return StatusCode(StatusCodes.Status500InternalServerError);
                    } 
                }
            }
            else { return NotFound(); }

        }
    }
}
