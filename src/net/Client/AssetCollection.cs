﻿//-----------------------------------------------------------------------
// <copyright file="AssetCollection.cs" company="Microsoft">Copyright 2012 Microsoft Corporation</copyright>
// <license>
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </license>


using System;
using System.Data.Services.Client;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

    
namespace Microsoft.WindowsAzure.MediaServices.Client
{
    /// <summary>
    /// Represents a collection of <see cref="IAsset"/>.
    /// </summary>
    public class AssetCollection : AssetBaseCollection
    {
        /// <summary>
        /// The set name for assets.
        /// </summary>
        internal const string AssetSet = "Assets";

        private readonly DataServiceContext _dataContext;
        private readonly CloudMediaContext _cloudMediaContext;
        private readonly Lazy<IQueryable<IAsset>> _assetQuery;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssetCollection"/> class.
        /// </summary>
        /// <param name="cloudMediaContext">The <seealso cref="CloudMediaContext"/> instance.</param>
        internal AssetCollection(CloudMediaContext cloudMediaContext)
        {
            this._cloudMediaContext = cloudMediaContext;
            this._dataContext = this._cloudMediaContext.DataContextFactory.CreateDataServiceContext();
            this._assetQuery = new Lazy<IQueryable<IAsset>>(() => this._dataContext.CreateQuery<AssetData>(AssetSet));
        }

        /// <summary>
        /// Gets the queryable collection of assets.
        /// </summary>
        protected override IQueryable<IAsset> Queryable
        {
            get { return this._assetQuery.Value; }
            set { throw new NotSupportedException(); }
        }

        /// <summary>
        /// Asynchronously creates an asset that does not contain any files and <see cref="AssetState"/> is Initialized.
        /// </summary>
        /// <param name="assetName">The asset name.</param>
        /// <param name="options">A <see cref="AssetCreationOptions"/> which will be associated with created asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// An <see cref="Task"/> of type <see cref="IAsset"/>created according to the specified creation <paramref name="options"/>.
        /// </returns>
        public override Task<IAsset> CreateAsync(string assetName, AssetCreationOptions options,CancellationToken cancellationToken)
        {
            AssetData emptyAsset = new AssetData
                                       {
                                           Name = assetName,
                                           Options = (int) options
                                       };

            emptyAsset.InitCloudMediaContext(this._cloudMediaContext);

            cancellationToken.ThrowIfCancellationRequested();
            DataServiceContext dataContext = this._cloudMediaContext.DataContextFactory.CreateDataServiceContext();
            dataContext.AddObject(AssetSet, emptyAsset);

            return dataContext
                .SaveChangesAsync(emptyAsset)
                .ContinueWith<IAsset>(
                    t =>
                        {
                            t.ThrowIfFaulted();
                            cancellationToken.ThrowIfCancellationRequested();

                            AssetData data = (AssetData) t.AsyncState;
                            if (options.HasFlag(AssetCreationOptions.StorageEncrypted))
                            {
                                using (var fileEncryption = new NullableFileEncryption())
                                {
                                    CreateStorageContentKey(data, fileEncryption, dataContext);
                                }
                            }

                            return data;
                        });
        }

        /// <summary>
        /// Creates an asset that does not contain any files and <see cref="AssetState"/> is Initialized. 
        /// </summary>
        /// <param name="assetName">The asset name.</param>
        /// <param name="options">A <see cref="AssetCreationOptions"/> which will be associated with created asset.</param>
        /// <returns>The created asset.</returns>
        public override IAsset Create(string assetName, AssetCreationOptions options)
        {
            try
            {
                Task<IAsset> task = this.CreateAsync(assetName, options,CancellationToken.None);
                task.Wait();

                return task.Result;
            }
            catch (AggregateException exception)
            {
                throw exception.InnerException;
            }
        }





        private static ContentKeyData CreateStorageContentKey(AssetData tempAsset, NullableFileEncryption fileEncryption, DataServiceContext dataContext)
        {
            // Create the content key.
            fileEncryption.Init();

            // Encrypt it for delivery to Nimbus.
            X509Certificate2 certToUse = ContentKeyCollection.GetCertificateToEncryptContentKey(dataContext, ContentKeyType.StorageEncryption);
            ContentKeyData contentKeyData = ContentKeyBaseCollection.CreateStorageContentKey(fileEncryption.FileEncryption, certToUse);

            dataContext.AddObject(ContentKeyCollection.ContentKeySet, contentKeyData);
            dataContext.SaveChanges();

            // Associate it with the asset.
            ((IAsset) tempAsset).ContentKeys.Add(contentKeyData);

            return contentKeyData;
        }

     
    }

}
