using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using tus_first.Models;

namespace tus_first.Services
{
    public class QueueService : IHostedService
    {
        BackgroundWorker _worker;
        string _connString;
        string _dbname;
        string _collectionName;
        string _tempDir;
        public QueueService(string connString, string dbname, string collectionName, string tempDir)
        {
            _dbname = dbname;
            _connString = connString;
            _collectionName = collectionName;
            _tempDir = tempDir;
            _worker = new BackgroundWorker();
            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += _worker_DoWork;
        }

        private void _worker_DoWork(object sender, DoWorkEventArgs e)
        {

            while (true)
            {
                var item = GetItem();

                var zipPath = item.filePath;

                UpdateStatus(item.Id, Status.Processing);
    
                ExtractZip(zipPath, item.tempDir);

                CopyFiles(item.tempDir, item.outputDir);

                UpdateStatus(item.Id, Status.Done);
            }
        }

        private void UpdateStatus(string id, Status status)
        {
            var filter = Builders<Item>.Filter.Eq("Id", id);
            var update = Builders<Item>.Update.Set(x => x.status, status);
            GetDB().GetCollection<Item>(_collectionName).UpdateOne(filter, update);
        }

        private void CopyFiles(string tempDir, string dstDir)
        {
            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            string imgDir = Path.Combine(dstDir, "images");
            if (!Directory.Exists(imgDir))
                Directory.CreateDirectory(imgDir);


            string labelDir = Path.Combine(dstDir, "labels");
            if (!Directory.Exists(labelDir))
                Directory.CreateDirectory(labelDir);

            string itemJson = System.IO.Path.Combine(tempDir, "item.json");

            Dictionary<string, string> items;
            using (FileStream openStream = File.OpenRead(itemJson))
            {
                items = System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string>>(openStream).Result;
            }
               

            var imglist = Path.Combine(dstDir, "img.txt");
            var labellist = Path.Combine(dstDir, "label.txt");
            using (StreamWriter img = new(imglist, append: true))
            {
                using (StreamWriter label = new(labellist, append: true))
                {

                    foreach (var item in items)
                    {
                        var imgPath = GetImgPath(item.Value, tempDir);
                        var imgname = Path.GetFileName(imgPath);
                        var imgCopyPath = Path.Combine(imgDir, imgname);
                        if (File.Exists(imgCopyPath))
                            continue;

                        File.Copy(imgPath, imgCopyPath);
                        img.WriteLine(imgCopyPath);

                        var labelname = $"{item.Key}.txt";
                        var labelPath = Path.Combine(tempDir, "labels", labelname);
                        var labelCopyPath = Path.Combine(labelDir, labelname);

                        File.Copy(labelPath, labelCopyPath);
                        label.WriteLine(labelCopyPath);
                    }
                }
            }
            File.Copy(labellist, Path.Combine(dstDir, "label_val.txt"));
            File.Copy(imglist, Path.Combine(dstDir, "img_val.txt"));
        }

        private string GetImgPath(string itemPath, string outputDir)
        {
            var elems = itemPath.ToString().Split('\\');
            string itempath = Path.Combine(outputDir, "items");
            for (int i = 0; i < elems.Length; i++)
            {
                string elem = elems[i];
                if (i == 0)
                {
                    elem = elem.Replace(":", "");
                }
                itempath = Path.Combine(itempath, elem);
            }

            if (!File.Exists(itempath)) throw new Exception($"image file not exists {itempath}");
            return itempath;
        }

        private void ExtractZip(string zipfile, string output_path)
        {
            if (!Directory.Exists(output_path))
                Directory.CreateDirectory(output_path);

            else
                return;

            using (FileStream fs = new FileStream(zipfile, FileMode.Open))
            {
                using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        var entry = archive.Entries[i];
                        string path = output_path + (entry.FullName[0] != '\\' ? '\\' + entry.FullName : entry.FullName);
                        string dirname = Path.GetDirectoryName(path);

                        if (File.Exists(path))
                            continue;

                        if (!Directory.Exists(dirname))
                            Directory.CreateDirectory(dirname);

                        if(path != dirname + '/')
                            entry.ExtractToFile(path);
                    }
                }
            }
        }

        private Item GetItem()
        {
            Item res = null;
            while (true)
            {
                res = GetDB()
                        .GetCollection<Item>(_collectionName)
                        .AsQueryable()
                        .Where(x => x.status == Status.Ready)
                        .Take(1)
                        .FirstOrDefault();

                if (res != null)
                    break;
            }

            return res;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                _worker.RunWorkerAsync();
            });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(() =>
            {
                _worker.CancelAsync();
            });
        }
        private IMongoDatabase GetDB()
        {
            var cli = new MongoClient(_connString);
            return cli.GetDatabase(_dbname);
        }
    }
}
