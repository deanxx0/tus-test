using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using tus_first.Services;
using tusdotnet;
using tusdotnet.Helpers;
using tusdotnet.Interfaces;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

namespace tus_first
{
    public class Startup
    {
        string _connString;
        string _dbname;
        string _collectionName;
        string _tempDir;
        string _datasetDir;
        string _tusDir;
        string _apiAddress;
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors();
            services.AddControllers();
            _apiAddress = Configuration.GetValue<string>("API_ADDRESS");
            _tusDir = Configuration.GetValue<string>("TUS_DIR");
            _tempDir = Configuration.GetValue<string>("TEMP_DIR");
            _datasetDir = Configuration.GetValue<string>("DATASET_DIR");
            var user = Configuration.GetValue<string>("DB_USER");
            var pw = Configuration.GetValue<string>("DB_PW");
            var host = Configuration.GetValue<string>("DB_HOST");
            _dbname = Configuration.GetValue<string>("DB_NAME");
            _connString = $"mongodb://{user}:{pw}@{host}/{_dbname}?authSource=admin";
            _collectionName = Configuration.GetValue<string>("COLLECTION_NAME");
            var tempDir = Configuration.GetValue<string>("TEMP_DIR");
            services.AddHostedService<QueueService>(serviceProvider => new QueueService(_connString, _dbname, _collectionName, tempDir));


            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "NAVIAISharp.WebApi.Servers.Training", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            
            app.Use((context, next) =>
            {
                // Default limit was changed some time ago. Should work by setting MaxRequestBodySize to null using ConfigureKestrel but this does not seem to work for IISExpress.
                // Source: https://github.com/aspnet/Announcements/issues/267
                context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = null;
                return next.Invoke();
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "tus_first v1"));
            }

            app.UseHttpsRedirection();
            app.UseCors(builder => builder
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowAnyOrigin()
                //.AllowCredentials()
                .WithExposedHeaders("file-name")
                .WithExposedHeaders(CorsHelper.GetExposedHeaders()));

            app.UseTus(httpContext => new DefaultTusConfiguration
            {
                Store = new TusDiskStore(_tusDir),
                UrlPath = "/files",
                Events = new Events
                {
                    OnBeforeCreateAsync = async eventContext =>
                    {
                        string acceessToken = eventContext.Metadata["access_token"].GetString(System.Text.Encoding.UTF8);
                        try
                        {
                            using (var client = new HttpClient())
                            {
                                client.BaseAddress = new Uri($"{_apiAddress}");
                                client.DefaultRequestHeaders.Accept.Clear();
                                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acceessToken);
                                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                                var response = client.GetAsync("user/confirm").Result;
                                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                                    throw new Exception("invalid token");
                            }
                        }
                        catch (Exception e)
                        {
                            eventContext.FailRequest(e.Message);
                        }
                    },

                    OnFileCompleteAsync = async eventContext =>
                    {
                        System.Console.WriteLine("upload complete!");
                        // eventContext.FileId is the id of the file that was uploaded.
                        // eventContext.Store is the data store that was used (in this case an instance of the TusDiskStore)

                        // A normal use case here would be to read the file and do some processing on it.
                        ITusFile file = await eventContext.GetFileAsync();

                        Dictionary<string, Metadata> metadata = await file.GetMetadataAsync(eventContext.CancellationToken);
                        string zipfileName = System.IO.Path.GetFileNameWithoutExtension(metadata["filename"].GetString(System.Text.Encoding.UTF8));

                        var orgPath = System.IO.Path.Combine(_tusDir, file.Id);
                        var dstPath = $"{orgPath}.zip";
                        if (System.IO.File.Exists(orgPath))
                        {
                            System.IO.File.Move(orgPath, dstPath);
                        }

                        var collection = new MongoDB.Driver.MongoClient(_connString)
                                            .GetDatabase(_dbname)
                                            .GetCollection<Models.Item>(_collectionName);

                        collection.InsertOne(new Models.Item()
                        {
                            filePath = dstPath,
                            tempDir = System.IO.Path.Combine(_tempDir, zipfileName),
                            outputDir = System.IO.Path.Combine(_datasetDir, zipfileName),
                            status = Models.Status.Ready,
                        });
                        System.Console.WriteLine("DB status ready!");

                        //var result = await DoSomeProcessing(file, eventContext.CancellationToken).ConfigureAwait(false);

                        //if (!result)
                        //{
                        //    //throw new MyProcessingException("Something went wrong during processing");
                        //}
                    }
                }
            });

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
