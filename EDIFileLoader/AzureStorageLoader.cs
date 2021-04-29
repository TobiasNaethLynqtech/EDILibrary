﻿// Copyright (c) 2017 Hochfrequenz Unternehmensberatung GmbH

using Azure.Storage.Blobs;

using EDILibrary;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EDIFileLoader
{
    public class AzureStorageLoader : EDILibrary.Interfaces.ITemplateLoader
    {
        protected string _accountName;
        protected string _accountKey;
        protected string _containerName;
        protected string _connectionString;


        protected BlobServiceClient _blobClient;
        protected BlobContainerClient _container;

        protected Dictionary<string, Dictionary<string, string>> Cache { get; set; } = new Dictionary<string, Dictionary<string, string>>();
        public AzureStorageLoader(string accountName, string accountKey, string containerName, string connectionString)
        {
            _accountKey = accountKey;
            _accountName = accountName;
            _containerName = containerName;
            // Parse the connection string and return a reference to the storage account.
            _blobClient = new BlobServiceClient(connectionString);

            _container = _blobClient.GetBlobContainerClient(_containerName);
        }
        public async Task PreloadCache()
        {
            await foreach (var prefixPage in _container.GetBlobsByHierarchyAsync(Azure.Storage.Blobs.Models.BlobTraits.Metadata, Azure.Storage.Blobs.Models.BlobStates.None).AsPages())
            {
                foreach (var prefix in prefixPage.Values)
                {
                    if (prefix.IsPrefix)
                    {
                        Cache.Add(prefix.Prefix, new Dictionary<string, string>());
                        await foreach (var blobPage in _container.GetBlobsByHierarchyAsync(Azure.Storage.Blobs.Models.BlobTraits.Metadata, Azure.Storage.Blobs.Models.BlobStates.None, prefix.Prefix).AsPages())
                        {
                            foreach (var blob in blobPage.Values)
                            {
                                if (blob.IsBlob)
                                {
                                    var blockBlob = _container.GetBlobClient(blob.Blob.Name);
                                    string text = await new StreamReader((await blockBlob.DownloadAsync()).Value.Content, Encoding.UTF8).ReadToEndAsync();
                                    string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
                                    if (text.StartsWith(_byteOrderMarkUtf8) && text[0] == _byteOrderMarkUtf8[0])
                                    {
                                        text = text.Remove(0, _byteOrderMarkUtf8.Length);
                                    }
                                    Cache[prefix.Prefix].TryAdd(blob.Blob.Name, text);
                                }
                            }
                        }
                    }
                }
            }
        }
        public async Task<string> LoadEDITemplate(EDIFileInfo info, string type)
        {
            if (Cache != null)
            {
                try
                {
                    return Cache["edi"][Path.Combine("edi", info.Format, info.Format + info.Version + "." + type)];
                }
                catch (Exception)
                {
                    return null;
                }
            }
            var blockBlob = _container.GetBlobClient(Path.Combine("edi", info.Format, info.Format + info.Version + "." + type));

            string text = await new StreamReader((await blockBlob.DownloadAsync()).Value.Content, Encoding.UTF8).ReadToEndAsync();
            string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            if (text.StartsWith(_byteOrderMarkUtf8) && text[0] == _byteOrderMarkUtf8[0])
            {
                text = text.Remove(0, _byteOrderMarkUtf8.Length);
            }
            return text;
        }
        public Task<string> LoadJSONTemplate(string fileName)
        {
            throw new NotSupportedException();
        }
        public async Task<string> LoadJSONTemplate(string formatPackage, string fileName)
        {
            if (Cache != null)
            {
                try
                {
                    return Cache["edi"][Path.Combine(formatPackage.Replace("/", ""), fileName.Replace("\\", "/"))];
                }
                catch (Exception)
                {
                    return null;
                }
            }
            var blockBlob = _container.GetBlobClient(System.IO.Path.Combine(formatPackage.Replace("/", ""), fileName).Replace("\\", "/"));

            string text = await new StreamReader((await blockBlob.DownloadAsync()).Value.Content, Encoding.UTF8).ReadToEndAsync();
            byte[] preamble = Encoding.UTF8.GetPreamble();
            string _byteOrderMarkUtf8 = Encoding.UTF8.GetString(preamble);
            if (text.StartsWith(_byteOrderMarkUtf8) && Encoding.UTF8.GetBytes(text)[0] == preamble[0])
            {
                text = text.Remove(0, _byteOrderMarkUtf8.Length);
            }
            return text;

        }
    }
}
