﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Edi.SyndicationFeedGenerator;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Configuration;
using Moonglade.Data.Entities;
using Moonglade.Data.Infrastructure;
using Moonglade.Data.Spec;
using Moonglade.Model;
using Moonglade.Model.Settings;

namespace Moonglade.Core
{
    public class SyndicationService : MoongladeService
    {
        private readonly BlogConfig _blogConfig;

        private readonly string _baseUrl;

        private readonly IRepository<Category> _categoryRepository;

        private readonly IRepository<Post> _postRepository;

        public SyndicationService(
            ILogger<SyndicationService> logger,
            IOptions<AppSettings> settings,
            BlogConfig blogConfig,
            BlogConfigurationService blogConfigurationService,
            IHttpContextAccessor httpContextAccessor,
            IRepository<Category> categoryRepository,
            IRepository<Post> postRepository) : base(logger: logger, settings: settings)
        {
            _blogConfig = blogConfig;
            _categoryRepository = categoryRepository;
            _postRepository = postRepository;
            _blogConfig.GetConfiguration(blogConfigurationService);

            var acc = httpContextAccessor;
            _baseUrl = $"{acc.HttpContext.Request.Scheme}://{acc.HttpContext.Request.Host}";
        }

        public async Task RefreshRssFilesForCategoryAsync(string categoryName)
        {
            Logger.LogInformation($"Start refreshing RSS feed for category {categoryName}.");
            var cat = await _categoryRepository.GetAsync(c => c.Title == categoryName);
            if (null != cat)
            {
                var itemCollection = GetPostsAsFeedItems(cat.Id);

                var rw = new SyndicationFeedGenerator
                {
                    HostUrl = _baseUrl,
                    HeadTitle = _blogConfig.FeedSettings.RssTitle,
                    HeadDescription = _blogConfig.FeedSettings.RssDescription,
                    Copyright = _blogConfig.FeedSettings.RssCopyright,
                    Generator = _blogConfig.FeedSettings.RssGeneratorName,
                    FeedItemCollection = itemCollection,
                    TrackBackUrl = _baseUrl,
                    MaxContentLength = 0
                };

                await rw.WriteRss20FileAsync($@"{AppDomain.CurrentDomain.GetData(Constants.DataDirectory)}\feed\posts-category-{categoryName}.xml");
                Logger.LogInformation($"Finished refreshing RSS feed for category {categoryName}.");
            }
            else
            {
                Logger.LogWarning($"Trying to refresh rss feed for category {categoryName} but {categoryName} is not found.");
            }
        }

        public async Task RefreshFeedFileForPostAsync(bool isAtom)
        {
            Logger.LogInformation("Start refreshing feed for posts.");
            var itemCollection = GetPostsAsFeedItems();

            var rw = new SyndicationFeedGenerator
            {
                HostUrl = _baseUrl,
                HeadTitle = _blogConfig.FeedSettings.RssTitle,
                HeadDescription = _blogConfig.FeedSettings.RssDescription,
                Copyright = _blogConfig.FeedSettings.RssCopyright,
                Generator = _blogConfig.FeedSettings.RssGeneratorName,
                FeedItemCollection = itemCollection,
                TrackBackUrl = _baseUrl,
                MaxContentLength = 0
            };

            if (isAtom)
            {
                Logger.LogInformation("Writing ATOM file.");
                await rw.WriteAtom10FileAsync($@"{AppDomain.CurrentDomain.GetData(Constants.DataDirectory)}\feed\posts-atom.xml");
            }
            else
            {
                Logger.LogInformation("Writing RSS file.");
                await rw.WriteRss20FileAsync($@"{AppDomain.CurrentDomain.GetData(Constants.DataDirectory)}\feed\posts.xml");
            }

            Logger.LogInformation("Finished writing feed for posts.");
        }

        private IReadOnlyList<SimpleFeedItem> GetPostsAsFeedItems(Guid? categoryId = null)
        {
            Logger.LogInformation($"{nameof(GetPostsAsFeedItems)} - {nameof(categoryId)}: {categoryId}");

            int? top = null;
            if (_blogConfig.FeedSettings.RssItemCount != 0)
            {
                top = _blogConfig.FeedSettings.RssItemCount;
            }

            var postSpec = new PostSpec(categoryId, top);
            var items = _postRepository.Select(postSpec, p => p.PostPublish.PubDateUtc != null ? new SimpleFeedItem
            {
                Id = p.Id.ToString(),
                Title = p.Title,
                PubDateUtc = p.PostPublish.PubDateUtc.Value,
                Description = p.ContentAbstract,
                Link = GetPostLink(p.PostPublish.PubDateUtc.Value, p.Slug),
                Author = _blogConfig.FeedSettings.AuthorName,
                AuthorEmail = _blogConfig.EmailConfiguration.AdminEmail,
                Categories = p.PostCategory.Select(pc => pc.Category.DisplayName).ToList()
            } : null);

            return items;
        }

        private string GetPostLink(DateTime pubDateUtc, string slug)
        {
            return $"{_baseUrl}/post/{pubDateUtc.Year}/{pubDateUtc.Month}/{pubDateUtc.Day}/{slug}";
        }
    }
}
