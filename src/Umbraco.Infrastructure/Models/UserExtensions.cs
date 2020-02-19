﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using Umbraco.Core.Cache;
using Umbraco.Core.IO;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Services;
using Umbraco.Core.Security;

namespace Umbraco.Core.Models
{
    public static class UserExtensions
    {
        /// <summary>
        /// Tries to lookup the user's Gravatar to see if the endpoint can be reached, if so it returns the valid URL
        /// </summary>
        /// <param name="user"></param>
        /// <param name="cache"></param>
        /// <param name="mediaFileSystem"></param>
        /// <returns>
        /// A list of 5 different sized avatar URLs
        /// </returns>
        public static string[] GetUserAvatarUrls(this IUser user, IAppCache cache, IMediaFileSystem mediaFileSystem, IImageUrlGenerator imageUrlGenerator)
        {
            // If FIPS is required, never check the Gravatar service as it only supports MD5 hashing.
            // Unfortunately, if the FIPS setting is enabled on Windows, using MD5 will throw an exception
            // and the website will not run.
            // Also, check if the user has explicitly removed all avatars including a Gravatar, this will be possible and the value will be "none"
            if (user.Avatar == "none" || CryptoConfig.AllowOnlyFipsAlgorithms)
            {
                return new string[0];
            }

            if (user.Avatar.IsNullOrWhiteSpace())
            {
                var gravatarHash = user.Email.GenerateHash<MD5>();
                var gravatarUrl = "https://www.gravatar.com/avatar/" + gravatarHash + "?d=404";

                //try Gravatar
                var gravatarAccess = cache.GetCacheItem<bool>("UserAvatar" + user.Id, () =>
                {
                    // Test if we can reach this URL, will fail when there's network or firewall errors
                    var request = (HttpWebRequest)WebRequest.Create(gravatarUrl);
                    // Require response within 10 seconds
                    request.Timeout = 10000;
                    try
                    {
                        using ((HttpWebResponse)request.GetResponse()) { }
                    }
                    catch (Exception)
                    {
                        // There was an HTTP or other error, return an null instead
                        return false;
                    }
                    return true;
                });

                if (gravatarAccess)
                {
                    return new[]
                    {
                        gravatarUrl  + "&s=30",
                        gravatarUrl  + "&s=60",
                        gravatarUrl  + "&s=90",
                        gravatarUrl  + "&s=150",
                        gravatarUrl  + "&s=300"
                    };
                }

                return new string[0];
            }

            //use the custom avatar
            var avatarUrl = mediaFileSystem.GetUrl(user.Avatar);
            return new[]
            {
                imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(avatarUrl) { ImageCropMode = "crop", Width = 30, Height = 30 }),
                imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(avatarUrl) { ImageCropMode = "crop", Width = 60, Height = 60 }),
                imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(avatarUrl) { ImageCropMode = "crop", Width = 90, Height = 90 }),
                imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(avatarUrl) { ImageCropMode = "crop", Width = 150, Height = 150 }),
                imageUrlGenerator.GetImageUrl(new ImageUrlGenerationOptions(avatarUrl) { ImageCropMode = "crop", Width = 300, Height = 300 })
            };

        }



        public static bool HasContentRootAccess(this IUser user, IEntityService entityService)
        {
            return ContentPermissionsHelper.HasPathAccess(Constants.System.RootString, user.CalculateContentStartNodeIds(entityService), Constants.System.RecycleBinContent);
        }

        public static bool HasContentBinAccess(this IUser user, IEntityService entityService)
        {
            return ContentPermissionsHelper.HasPathAccess(Constants.System.RecycleBinContentString, user.CalculateContentStartNodeIds(entityService), Constants.System.RecycleBinContent);
        }

        public static bool HasMediaRootAccess(this IUser user, IEntityService entityService)
        {
            return ContentPermissionsHelper.HasPathAccess(Constants.System.RootString, user.CalculateMediaStartNodeIds(entityService), Constants.System.RecycleBinMedia);
        }

        public static bool HasMediaBinAccess(this IUser user, IEntityService entityService)
        {
            return ContentPermissionsHelper.HasPathAccess(Constants.System.RecycleBinMediaString, user.CalculateMediaStartNodeIds(entityService), Constants.System.RecycleBinMedia);
        }

        public static bool HasPathAccess(this IUser user, IContent content, IEntityService entityService)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return ContentPermissionsHelper.HasPathAccess(content.Path, user.CalculateContentStartNodeIds(entityService), Constants.System.RecycleBinContent);
        }

        public static bool HasPathAccess(this IUser user, IMedia media, IEntityService entityService)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            return ContentPermissionsHelper.HasPathAccess(media.Path, user.CalculateMediaStartNodeIds(entityService), Constants.System.RecycleBinMedia);
        }

        public static bool HasContentPathAccess(this IUser user, IUmbracoEntity entity, IEntityService entityService)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            return ContentPermissionsHelper.HasPathAccess(entity.Path, user.CalculateContentStartNodeIds(entityService), Constants.System.RecycleBinContent);
        }

        public static bool HasMediaPathAccess(this IUser user, IUmbracoEntity entity, IEntityService entityService)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            return ContentPermissionsHelper.HasPathAccess(entity.Path, user.CalculateMediaStartNodeIds(entityService), Constants.System.RecycleBinMedia);
        }

        /// <summary>
        /// Determines whether this user has access to view sensitive data
        /// </summary>
        /// <param name="user"></param>
        public static bool HasAccessToSensitiveData(this IUser user)
        {
            if (user == null) throw new ArgumentNullException("user");
            return user.Groups != null && user.Groups.Any(x => x.Alias == Constants.Security.SensitiveDataGroupAlias);
        }

        // calc. start nodes, combining groups' and user's, and excluding what's in the bin
        public static int[] CalculateContentStartNodeIds(this IUser user, IEntityService entityService)
        {
            const string cacheKey = "AllContentStartNodes";
            //try to look them up from cache so we don't recalculate
            var valuesInUserCache = FromUserCache<int[]>(user, cacheKey);
            if (valuesInUserCache != null) return valuesInUserCache;

            var gsn = user.Groups.Where(x => x.StartContentId.HasValue).Select(x => x.StartContentId.Value).Distinct().ToArray();
            var usn = user.StartContentIds;
            var vals = CombineStartNodes(UmbracoObjectTypes.Document, gsn, usn, entityService);
            ToUserCache(user, cacheKey, vals);
            return vals;
        }

        // calc. start nodes, combining groups' and user's, and excluding what's in the bin
        public static int[] CalculateMediaStartNodeIds(this IUser user, IEntityService entityService)
        {
            const string cacheKey = "AllMediaStartNodes";
            //try to look them up from cache so we don't recalculate
            var valuesInUserCache = FromUserCache<int[]>(user, cacheKey);
            if (valuesInUserCache != null) return valuesInUserCache;

            var gsn = user.Groups.Where(x => x.StartMediaId.HasValue).Select(x => x.StartMediaId.Value).Distinct().ToArray();
            var usn = user.StartMediaIds;
            var vals = CombineStartNodes(UmbracoObjectTypes.Media, gsn, usn, entityService);
            ToUserCache(user, cacheKey, vals);
            return vals;
        }

        public static string[] GetMediaStartNodePaths(this IUser user, IEntityService entityService)
        {
            const string cacheKey = "MediaStartNodePaths";
            //try to look them up from cache so we don't recalculate
            var valuesInUserCache = FromUserCache<string[]>(user, cacheKey);
            if (valuesInUserCache != null) return valuesInUserCache;

            var startNodeIds = user.CalculateMediaStartNodeIds(entityService);
            var vals = entityService.GetAllPaths(UmbracoObjectTypes.Media, startNodeIds).Select(x => x.Path).ToArray();
            ToUserCache(user, cacheKey, vals);
            return vals;
        }

        public static string[] GetContentStartNodePaths(this IUser user, IEntityService entityService)
        {
            const string cacheKey = "ContentStartNodePaths";
            //try to look them up from cache so we don't recalculate
            var valuesInUserCache = FromUserCache<string[]>(user, cacheKey);
            if (valuesInUserCache != null) return valuesInUserCache;

            var startNodeIds = user.CalculateContentStartNodeIds(entityService);
            var vals = entityService.GetAllPaths(UmbracoObjectTypes.Document, startNodeIds).Select(x => x.Path).ToArray();
            ToUserCache(user, cacheKey, vals);
            return vals;
        }

        private static T FromUserCache<T>(IUser user, string cacheKey)
            where T: class
        {
            if (!(user is User entityUser)) return null;

            lock (entityUser.AdditionalDataLock)
            {
                return entityUser.AdditionalData.TryGetValue(cacheKey, out var allContentStartNodes)
                    ? allContentStartNodes as T
                    : null;
            }
        }

        private static void ToUserCache<T>(IUser user, string cacheKey, T vals)
            where T: class
        {
            if (!(user is User entityUser)) return;

            lock (entityUser.AdditionalDataLock)
            {
                entityUser.AdditionalData[cacheKey] = vals;
            }
        }

        private static bool StartsWithPath(string test, string path)
        {
            return test.StartsWith(path) && test.Length > path.Length && test[path.Length] == ',';
        }

        private static string GetBinPath(UmbracoObjectTypes objectType)
        {
            var binPath = Constants.System.Root + ",";
            switch (objectType)
            {
                case UmbracoObjectTypes.Document:
                    binPath += Constants.System.RecycleBinContent;
                    break;
                case UmbracoObjectTypes.Media:
                    binPath += Constants.System.RecycleBinMedia;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(objectType));
            }
            return binPath;
        }

        internal static int[] CombineStartNodes(UmbracoObjectTypes objectType, int[] groupSn, int[] userSn, IEntityService entityService)
        {
            // assume groupSn and userSn each don't contain duplicates

            var asn = groupSn.Concat(userSn).Distinct().ToArray();
            var paths = asn.Length > 0
                ? entityService.GetAllPaths(objectType, asn).ToDictionary(x => x.Id, x => x.Path)
                : new Dictionary<int, string>();

            paths[Constants.System.Root] = Constants.System.RootString; // entityService does not get that one

            var binPath = GetBinPath(objectType);

            var lsn = new List<int>();
            foreach (var sn in groupSn)
            {
                if (paths.TryGetValue(sn, out var snp) == false) continue; // ignore rogue node (no path)

                if (StartsWithPath(snp, binPath)) continue; // ignore bin

                if (lsn.Any(x => StartsWithPath(snp, paths[x]))) continue; // skip if something above this sn
                lsn.RemoveAll(x => StartsWithPath(paths[x], snp)); // remove anything below this sn
                lsn.Add(sn);
            }

            var usn = new List<int>();
            foreach (var sn in userSn)
            {
                if (paths.TryGetValue(sn, out var snp) == false) continue; // ignore rogue node (no path)

                if (StartsWithPath(snp, binPath)) continue; // ignore bin

                if (usn.Any(x => StartsWithPath(paths[x], snp))) continue; // skip if something below this sn
                usn.RemoveAll(x => StartsWithPath(snp, paths[x])); // remove anything above this sn
                usn.Add(sn);
            }

            foreach (var sn in usn)
            {
                var snp = paths[sn]; // has to be here now
                lsn.RemoveAll(x => StartsWithPath(snp, paths[x]) || StartsWithPath(paths[x], snp)); // remove anything above or below this sn
                lsn.Add(sn);
            }

            return lsn.ToArray();
        }
    }
}
