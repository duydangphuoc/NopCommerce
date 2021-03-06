using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Nop.Core;
using Nop.Core.Plugins;
using Nop.Services.Themes;

namespace Nop.Services.Plugins
{
    /// <summary>
    /// Represents the implementation of a service for uploading application extensions (plugins or themes)
    /// </summary>
    public class UploadService : IUploadService
    {
        #region Fields

        protected readonly IThemeProvider _themeProvider;

        #endregion

        #region Ctor

        public UploadService(IThemeProvider themeProvider)
        {
            this._themeProvider = themeProvider;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Get information about the uploaded items in the archive
        /// </summary>
        /// <param name="archivePath">Path to the archive</param>
        /// <returns>List of an uploaded item</returns>
        protected virtual IList<UploadedItem> GetUploadedItems(string archivePath)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                //try to get the entry containing information about the uploaded items 
                var uploadedItemsFileEntry = archive.Entries
                    .FirstOrDefault(entry => entry.Name.Equals(UploadedItemsFileName, StringComparison.InvariantCultureIgnoreCase)
                        && string.IsNullOrEmpty(Path.GetDirectoryName(entry.FullName)));
                if (uploadedItemsFileEntry == null)
                    return null;

                //read the content of this entry if exists
                using (var unzippedEntryStream = uploadedItemsFileEntry.Open())
                    using (var reader = new StreamReader(unzippedEntryStream))
                        return JsonConvert.DeserializeObject<IList<UploadedItem>>(reader.ReadToEnd());
            }
        }

        /// <summary>
        /// Upload single item from the archive into the physical directory
        /// </summary>
        /// <param name="archivePath">Path to the archive</param>
        /// <returns>Uploaded item descriptor</returns>
        protected virtual IDescriptor UploadSingleItem(string archivePath)
        {
            //get path to the plugins directory
            var pluginsDirectory = CommonHelper.MapPath(PluginManager.PluginsPath);

            //get path to the themes directory
            var themesDirectory = string.Empty;
            if (!string.IsNullOrEmpty(_themeProvider.ThemesPath))
                themesDirectory = CommonHelper.MapPath(_themeProvider.ThemesPath);

            IDescriptor descriptor = null;
            var uploadedItemDirectoryName = string.Empty;
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                //the archive should contain only one root directory (the plugin one or the theme one)
                var rootDirectories = archive.Entries.Where(entry => entry.FullName.Count(ch => ch == '/') == 1 && entry.FullName.EndsWith("/")).ToList();
                if (rootDirectories.Count != 1)
                {
                    throw new Exception($"The archive should contain only one root plugin or theme directory. " +
                        $"For example, Payments.PayPalDirect or DefaultClean. " +
                        $"To upload multiple items, the archive should have the '{UploadedItemsFileName}' file in the root");
                }

                //get directory name (remove the ending /)
                uploadedItemDirectoryName = rootDirectories.First().FullName.TrimEnd('/');

                //try to get descriptor of the uploaded item
                foreach (var entry in archive.Entries)
                {
                    //whether it's a plugin descriptor
                    var isPluginDescriptor = entry.FullName
                        .Equals($"{uploadedItemDirectoryName}/{PluginManager.PluginDescriptionFileName}", StringComparison.InvariantCultureIgnoreCase);

                    //or whether it's a theme descriptor
                    var isThemeDescriptor = entry.FullName
                        .Equals($"{uploadedItemDirectoryName}/{_themeProvider.ThemeDescriptionFileName}", StringComparison.InvariantCultureIgnoreCase);

                    if (!isPluginDescriptor && !isThemeDescriptor)
                        continue;

                    using (var unzippedEntryStream = entry.Open())
                    {
                        using (var reader = new StreamReader(unzippedEntryStream))
                        {
                            //whether a plugin is upload 
                            if (isPluginDescriptor)
                            {
                                descriptor = PluginManager.GetPluginDescriptorFromText(reader.ReadToEnd());

                                //ensure that the plugin current version is supported
                                if (!(descriptor as PluginDescriptor).SupportedVersions.Contains(NopVersion.CurrentVersion))
                                    throw new Exception($"This plugin doesn't support the current version - {NopVersion.CurrentVersion}");
                            }

                            //or whether a theme is upload 
                            if (isThemeDescriptor)
                                descriptor = _themeProvider.GetThemeDescriptorFromText(reader.ReadToEnd());

                            break;
                        }
                    }
                }
            }

            if (descriptor == null)
                throw new Exception("No descriptor file is found. It should be in the root of the archive.");

            if (string.IsNullOrEmpty(uploadedItemDirectoryName))
                throw new Exception($"Cannot get the {(descriptor is PluginDescriptor ? "plugin" : "theme")} directory name");

            //get path to upload
            var directoryPath = descriptor is PluginDescriptor ? pluginsDirectory : themesDirectory;
            var pathToUpload = Path.Combine(directoryPath, uploadedItemDirectoryName);

            //ensure it's a new directory (e.g. some old files are not required when re-uploading a plugin)
            //furthermore, zip extract functionality cannot override existing files
            //but there could deletion issues (related to file locking, etc). In such cases the directory should be deleted manually
            if (System.IO.Directory.Exists(pathToUpload))
                CommonHelper.DeleteDirectory(pathToUpload);

            //unzip archive
            ZipFile.ExtractToDirectory(archivePath, directoryPath);

            return descriptor;
        }

        /// <summary>
        /// Upload multiple items from the archive into the physical directory
        /// </summary>
        /// <param name="archivePath">Path to the archive</param>
        /// <param name="uploadedItems">Uploaded items</param>
        /// <returns>List of uploaded items descriptor</returns>
        protected virtual IList<IDescriptor> UploadMultipleItems(string archivePath, IList<UploadedItem> uploadedItems)
        {
            //get path to the plugins directory
            var pluginsDirectory = CommonHelper.MapPath(PluginManager.PluginsPath);

            //get path to the themes directory
            var themesDirectory = string.Empty;
            if (!string.IsNullOrEmpty(_themeProvider.ThemesPath))
                themesDirectory = CommonHelper.MapPath(_themeProvider.ThemesPath);

            //get descriptors of items contained in the archive
            var descriptors = new List<IDescriptor>();
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var item in uploadedItems)
                {
                    if (!item.Type.HasValue)
                        continue;

                    //ensure that the current version of nopCommerce is supported
                    if (!item.SupportedVersions?.Contains(NopVersion.CurrentVersion) ?? true)
                        continue;

                    //the item path should end with a slash
                    var itemPath = $"{item.DirectoryPath?.TrimEnd('/')}/";

                    //get path to the descriptor entry in the archive
                    var descriptorPath = string.Empty;
                    if (item.Type == UploadedItemType.Plugin)
                        descriptorPath = $"{itemPath}{PluginManager.PluginDescriptionFileName}";

                    if (item.Type == UploadedItemType.Theme && !string.IsNullOrEmpty(_themeProvider.ThemeDescriptionFileName))
                        descriptorPath = $"{itemPath}{_themeProvider.ThemeDescriptionFileName}";

                    //try to get the descriptor entry
                    var descriptorEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.Equals(descriptorPath, StringComparison.InvariantCultureIgnoreCase));
                    if (descriptorEntry == null)
                        continue;

                    //try to get descriptor of the uploaded item
                    IDescriptor descriptor = null;
                    using (var unzippedEntryStream = descriptorEntry.Open())
                    {
                        using (var reader = new StreamReader(unzippedEntryStream))
                        {
                            //whether a plugin is upload 
                            if (item.Type == UploadedItemType.Plugin)
                                descriptor = PluginManager.GetPluginDescriptorFromText(reader.ReadToEnd());

                            //or whether a theme is upload 
                            if (item.Type == UploadedItemType.Theme)
                                descriptor = _themeProvider.GetThemeDescriptorFromText(reader.ReadToEnd());
                        }
                    }
                    if (descriptor == null)
                        continue;

                    //ensure that the plugin current version is supported
                    if (descriptor is PluginDescriptor pluginDescriptor && !pluginDescriptor.SupportedVersions.Contains(NopVersion.CurrentVersion))
                        continue;
                    
                    //get path to upload
                    var uploadedItemDirectoryName = Path.GetFileName(itemPath.TrimEnd('/'));
                    var pathToUpload = Path.Combine(item.Type == UploadedItemType.Plugin ? pluginsDirectory : themesDirectory, uploadedItemDirectoryName);

                    //ensure it's a new directory (e.g. some old files are not required when re-uploading a plugin or a theme)
                    //furthermore, zip extract functionality cannot override existing files
                    //but there could deletion issues (related to file locking, etc). In such cases the directory should be deleted manually
                    if (System.IO.Directory.Exists(pathToUpload))
                        CommonHelper.DeleteDirectory(pathToUpload);

                    //unzip entries into files
                    var entries = archive.Entries.Where(entry => entry.FullName.StartsWith(itemPath, StringComparison.InvariantCultureIgnoreCase));
                    foreach (var entry in entries)
                    {
                        //get name of the file
                        var fileName = entry.FullName.Substring(itemPath.Length);
                        if (string.IsNullOrEmpty(fileName))
                            continue;
                        
                        var filePath = Path.Combine(pathToUpload, fileName.Replace("/", "\\"));
                        var directoryPath = Path.GetDirectoryName(filePath);

                        //whether the file directory is already exists, otherwise create the new one
                        if (!System.IO.Directory.Exists(directoryPath))
                            System.IO.Directory.CreateDirectory(directoryPath);

                        //unzip entry to the file (ignore directory entries)
                        if (!filePath.Equals($"{directoryPath}\\", StringComparison.InvariantCultureIgnoreCase))
                            entry.ExtractToFile(filePath);
                    }

                    //item is uploaded
                    descriptors.Add(descriptor);
                }
            }

            return descriptors;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Upload plugins and/or themes
        /// </summary>
        /// <param name="archivefile">Archive file</param>
        /// <returns>List of uploaded items descriptor</returns>
        public virtual IList<IDescriptor> UploadPluginsAndThemes(IFormFile archivefile)
        {
            if (archivefile == null)
                throw new ArgumentNullException(nameof(archivefile));

            var zipFilePath = string.Empty;
            var descriptors = new List<IDescriptor>();
            try
            {
                //only zip archives are supported
                if (!Path.GetExtension(archivefile.FileName)?.Equals(".zip", StringComparison.InvariantCultureIgnoreCase) ?? true)
                    throw new Exception("Only zip archives are supported");

                //ensure that temp directory is created
                var tempDirectory = CommonHelper.MapPath(UploadsTempPath);
                System.IO.Directory.CreateDirectory(new DirectoryInfo(tempDirectory).FullName);

                //copy original archive to the temp directory
                zipFilePath = Path.Combine(tempDirectory, archivefile.FileName);
                using (var fileStream = new FileStream(zipFilePath, FileMode.Create))
                    archivefile.CopyTo(fileStream);

                //try to get information about the uploaded items from the JSON file in the root of the archive
                //you can find a sample of such descriptive file in Libraries\Nop.Core\Plugins\Samples\
                var uploadedItems = GetUploadedItems(zipFilePath);
                if (!uploadedItems?.Any() ?? true)
                {
                    //JSON file doesn't exist, so there is a single plugin or theme in the archive, just unzip it
                    descriptors.Add(UploadSingleItem(zipFilePath));
                }
                else
                    descriptors.AddRange(UploadMultipleItems(zipFilePath, uploadedItems));
            }
            finally
            {
                //delete temporary file
                if (!string.IsNullOrEmpty(zipFilePath))
                    File.Delete(zipFilePath);
            }

            return descriptors;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the path to temp directory with uploads
        /// </summary>
        public string UploadsTempPath => "~/App_Data/TempUploads";

        /// <summary>
        /// Gets the name of the file containing information about the uploaded items
        /// </summary>
        public string UploadedItemsFileName => "uploadedItems.json";

        #endregion

        #region Nested classes

        /// <summary>
        /// Represents uploaded item (plugin or theme) details 
        /// </summary>
        public class UploadedItem
        {
            /// <summary>
            /// Gets or sets the type of an uploaded item
            /// </summary>
            [JsonProperty(PropertyName = "Type")]
            [JsonConverter(typeof(StringEnumConverter))]
            public UploadedItemType? Type { get; set; }

            /// <summary>
            /// Gets or sets the system name
            /// </summary>
            [JsonProperty(PropertyName = "SystemName")]
            public string SystemName { get; set; }

            /// <summary>
            /// Gets or sets supported versions of nopCommerce
            /// </summary>
            [JsonProperty(PropertyName = "SupportedVersion")]
            public string SupportedVersions { get; set; }

            /// <summary>
            /// Gets or sets the path to binary files directory
            /// </summary>
            [JsonProperty(PropertyName = "DirectoryPath")]
            public string DirectoryPath { get; set; }

            /// <summary>
            /// Gets or sets the path to source files directory
            /// </summary>
            [JsonProperty(PropertyName = "SourceDirectoryPath")]
            public string SourceDirectoryPath { get; set; }
        }

        /// <summary>
        /// Uploaded item type enumeration
        /// </summary>
        public enum UploadedItemType
        {
            /// <summary>
            /// Plugin
            /// </summary>
            [EnumMember(Value = "Plugin")]
            Plugin,

            /// <summary>
            /// Theme
            /// </summary>
            [EnumMember(Value = "Theme")]
            Theme
        }

        #endregion
    }
}