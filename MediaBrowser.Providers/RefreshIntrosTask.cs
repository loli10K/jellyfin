﻿using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Providers
{
    /// <summary>
    /// Class RefreshIntrosTask
    /// </summary>
    public class RefreshIntrosTask : ILibraryPostScanTask
    {
        /// <summary>
        /// The _library manager
        /// </summary>
        private readonly ILibraryManager _libraryManager;
        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RefreshIntrosTask"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        public RefreshIntrosTask(ILibraryManager libraryManager, ILogger logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        /// <summary>
        /// Runs the specified progress.
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var files = _libraryManager.GetAllIntroFiles().ToList();

            var numComplete = 0;

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await RefreshIntro(file, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error refreshing intro {0}", ex, file);
                }

                numComplete++;
                double percent = numComplete;
                percent /= files.Count;
                progress.Report(percent * 100);
            }
        }

        /// <summary>
        /// Refreshes the intro.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task RefreshIntro(string path, CancellationToken cancellationToken)
        {
            var item = _libraryManager.ResolvePath(FileSystem.GetFileSystemInfo(path));

            if (item == null)
            {
                _logger.Error("Intro resolver returned null for {0}", path);
                return;
            }

            var dbItem = _libraryManager.RetrieveItem(item.Id);
            var isNewItem = false;

            if (dbItem != null)
            {
                dbItem.ResetResolveArgs(item.ResolveArgs);
                item = dbItem;
            }
            else
            {
                isNewItem = true;
            }

            // Force the save if it's a new item
            await item.RefreshMetadata(cancellationToken, isNewItem).ConfigureAwait(false);
        }
    }
}