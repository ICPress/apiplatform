using Microsoft.AspNetCore.Mvc;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Cache.Query;
using Apache.Ignite.Core.Client.Cache;
using Apache.Ignite.Core.Cache.Configuration;
using JWT.Builder;
using JWT.Algorithms;
using System.Text.Json;
using Slugify;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using MySql.Data.MySqlClient;
using LanguageDetection;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Generic;
using System.Collections.ObjectModel;
namespace apiplatform.Controllers;


[ApiController]
[Route("[controller]")]
public class NotificationController : ControllerBase
{
    private readonly ILogger<TokenController> _logger;
    private readonly ServerSettings _serverSettings;

    public NotificationController(ILogger<TokenController> logger, ServerSettings serverSettings)
    {
        _logger = logger;
        _serverSettings = serverSettings;
    }


    [Authorize]
    [HttpGet("V2/{username}")]
    public async Task<List<NotificationModel>>? GetNotificationsV2(string username, uint? startIndex = null, int count = 5)
    {
        var response = new List<NotificationModel>();
        if (username == null)
        {
            _logger.LogError("Attemted to fetch followed articles without username, returning empty result!");
            return response;
        }
        var role = ConfigUtil.VerifyUserNameFromClaimAndGetRole(username, HttpContext.User.Identity as ClaimsIdentity);
        if (role == null)
        {
            _logger.LogError("Attemted to fetch notifications with wrong username:" + username + ", returning empty result!");
            throw new UnauthorizedAccessException("Unauthorized!");
        }
        using MySqlConnection connection = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.GORSE, _serverSettings));
        using MySqlConnection connectionStory = new MySqlConnection(ConfigUtil.GetMysqlConnectionStringForDatabase(ConfigUtil.TargetDatabase.STORYPOP, _serverSettings));
        await connectionStory.OpenAsync();
        await connection.OpenAsync();
        try
        {
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT notification_id, type, additional_data,transaction_description_type,notification_read, DATE_FORMAT(available_from, '%Y-%m-%dT%TZ') as available_from FROM user_notification  WHERE username = @username AND notification_id < coalesce(@startIndex,4294967295) ORDER BY notification_id DESC LIMIT @count";
            mySqlCommand.Parameters.AddWithValue("@username", username);
            mySqlCommand.Parameters.AddWithValue("@startIndex", startIndex);
            mySqlCommand.Parameters.AddWithValue("@count", count);
            mySqlCommand.Connection = connectionStory;
            using (var reader = mySqlCommand.ExecuteReader())
            {
                while (await reader.ReadAsync())
                {

                    uint notificationId = reader.GetUInt32("notification_id");
                    ushort type = reader.GetUInt16("type");
                    string additionalData = reader.GetString("additional_data");
                    uint transactionDescriptionType = reader.GetUInt32("transaction_description_type");
                    bool notificationRead = reader.GetBoolean("notification_read");
                    string availableFrom = reader.GetString("available_from");
                    var notification = new NotificationModel(notificationId, type, additionalData, transactionDescriptionType, availableFrom, notificationRead);
                    response.Add(notification);
                }
                await reader.CloseAsync();
            }
            var userMessageNotifications = response.Where(x => x.NotificationType == (ushort)NotificationType.MESSAGE_RECEIVED);
            var userCommentNotifications = response.Where(x => x.NotificationType == (uint)NotificationType.COMMENT_LIKE_RECEIVED ||
             x.NotificationType == (uint)NotificationType.COMMENT_REPLY_RECEIVED || x.NotificationType == (uint)NotificationType.COMMENT_RECEIVED);
            var userActionNotifications = response.Where(x => x.NotificationType == (ushort)NotificationType.FOLLOW_RECEIVED);
            var userLikeNotifications = response.Where(x => x.NotificationType == (uint)NotificationType.LIKE_RECEIVED);
            var userArticleNotifications = response.Where(x => x.NotificationType == (uint)NotificationType.ARTICLE_REJECTED);
            foreach (var item in userActionNotifications)
            {
                item.TriggerAuthor = item.AdditionalData;
            }
            foreach (var item in userCommentNotifications.Union(userLikeNotifications).Union(userMessageNotifications))
            {
                item.TriggerAuthor = item.AdditionalData.Substring(0, item.AdditionalData.IndexOf(":"));
            }
            var unionAuthorItem = userActionNotifications.Union(userCommentNotifications).Union(userLikeNotifications).Union(userMessageNotifications).ToList();
            if (unionAuthorItem.Any()) // UserNotification == 2 (NotificationType)
            {
                var mySqlCommandBadge = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandBadge.CommandText = "SELECT username, profile_icon from users WHERE username IN (" + string.Join(",", unionAuthorItem.Select(x => x.TriggerAuthor).Distinct().Select(_ => "?")) + ")";
                mySqlCommandBadge.Connection = connectionStory;
                mySqlCommandBadge.Parameters.AddRange(unionAuthorItem.Select(x => x.TriggerAuthor).Distinct().Select(x => new MySqlParameter("", x)).ToArray());
                string? authorBadgeMetadata;
                string badgeUsername;
                using var reader = mySqlCommandBadge.ExecuteReader();
                while (await reader.ReadAsync())
                {
                    badgeUsername = reader.GetString("username");
                    authorBadgeMetadata = reader.IsDBNull(reader.GetOrdinal("profile_icon")) ? null : reader.GetString("profile_icon");
                    if (authorBadgeMetadata == null) continue;
                    foreach (var authorArticle in unionAuthorItem.Where(x => x.TriggerAuthor == badgeUsername))
                    {
                        authorArticle.ProfileIcon = authorBadgeMetadata;
                    }
                }
                await reader.CloseAsync();
            }
            if (userLikeNotifications.Union(userArticleNotifications).Any())
            {
                using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(_serverSettings));
                var generalCache = client.GetOrCreateCache<string, StorySavedModel>("storyarticle");
                foreach (var likeNotification in userLikeNotifications.Union(userArticleNotifications))
                {
                    var indexSlugTitleSeparator = likeNotification.AdditionalData.IndexOf(":");
                    if (indexSlugTitleSeparator < 0) continue;
                    var slugTitle = likeNotification.AdditionalData.Substring(0, indexSlugTitleSeparator);

                    StorySavedModel? storyModel = await ArticleUtil.TryGetWithFallbackAsync(generalCache, slugTitle, connectionStory, _logger);

                    if (storyModel != null)
                    {
                        likeNotification.StoryTitle = (storyModel.StoryTitle == null || storyModel.StoryTitle.Length == 0)
                            ? storyModel.EmptyTitle
                            : storyModel.StoryTitle;
                    }
                }
            }

            if (userCommentNotifications.Any())
            {
                foreach (var userCommentNotification in userCommentNotifications)
                {
                    var slugTitleCommentUUIDKeyPair = userCommentNotification.AdditionalData.Split(":");
                    var triggerUsername = slugTitleCommentUUIDKeyPair[0];
                    var slugTitle = slugTitleCommentUUIDKeyPair[1];
                    userCommentNotification.StoryTitle = slugTitle;
                    var triggerCommentUUID = slugTitleCommentUUIDKeyPair[2];
                    var replyToCommentUUID = (slugTitleCommentUUIDKeyPair.Length == 4) ? slugTitleCommentUUIDKeyPair[3] : null;
                    ArticleCommentPublished? targetComment = null;
                    ArticleCommentPublished? replyToComment = null;
                    ArticleCommentPublished? notificationReply = null;
                    var mySqlCommand4 = new MySql.Data.MySqlClient.MySqlCommand();
                    if (userCommentNotification.NotificationType == (ushort)NotificationType.COMMENT_REPLY_RECEIVED || userCommentNotification.NotificationType == (uint)NotificationType.COMMENT_RECEIVED)
                    {
                        mySqlCommand4.CommandText = "SELECT BIN_TO_UUID(comment_uuid) as comment_uuid, BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid, slug_title, username,comment,hidden,deleted,lang_code,DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at FROM user_story_comment WHERE"
                        + " slug_title = @slug_title AND ( comment_uuid IN (UUID_TO_BIN(@triggerCommentUUID), UUID_TO_BIN(@replyToCommentUUID)) OR comment_uuid IN ( " +
                        " SELECT comment_uuid FROM user_story_comment WHERE slug_title = @slug_title AND username=@currentUsername AND original_reply_to_comment_uuid = UUID_TO_BIN(@triggerCommentUUID)))";
                        mySqlCommand4.Parameters.AddWithValue("@replyToCommentUUID", replyToCommentUUID);
                        mySqlCommand4.Parameters.AddWithValue("@currentUsername", username);
                    }
                    else
                    {
                        mySqlCommand4.CommandText = "SELECT BIN_TO_UUID(comment_uuid) as comment_uuid, BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid, slug_title, username,comment,hidden,deleted,lang_code,DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at FROM user_story_comment WHERE"
                        + " slug_title = @slug_title AND comment_uuid = UUID_TO_BIN(@triggerCommentUUID)";
                    }
                    mySqlCommand4.Parameters.AddWithValue("@triggerCommentUUID", triggerCommentUUID);
                    mySqlCommand4.Parameters.AddWithValue("@slug_title", slugTitle);
                    mySqlCommand4.Connection = connectionStory;
                    using (var readerComment = mySqlCommand4.ExecuteReader())
                    {
                        while (await readerComment.ReadAsync())
                        {
                            string commentUuid = readerComment.GetString("comment_uuid");
                            string? reply_to_comment_uuid = readerComment.IsDBNull(readerComment.GetOrdinal("reply_to_comment_uuid")) ? null : readerComment.GetString("reply_to_comment_uuid");
                            string commentUsername = readerComment.GetString("username");
                            string comment;
                            bool hidden = readerComment.GetBoolean("hidden");
                            bool deleted = readerComment.GetBoolean("deleted");
                            string langCode = readerComment.GetString("lang_code");
                            string timestamp = readerComment.GetString("published_at");
                            if (deleted) comment = "";
                            else comment = readerComment.GetString("comment");
                            var commentPublished = new ArticleCommentPublished(commentUsername, slugTitle, commentUuid, reply_to_comment_uuid,
                             null, comment, hidden, deleted, langCode, timestamp, false);
                            if (commentUuid.Equals(triggerCommentUUID)) targetComment = commentPublished;
                            else if (commentUuid.Equals(replyToCommentUUID)) replyToComment = commentPublished;
                            else notificationReply = commentPublished;
                        }
                        await readerComment.CloseAsync();
                    }
                    if (targetComment != null)
                    {
                        var entry = new ArticleCommentLikeReplyNotification(targetComment, replyToComment, notificationReply);
                        userCommentNotification.AdditionalData = JsonSerializer.Serialize(entry);
                    }
                }
            }
            if (userMessageNotifications.Any())
            {
                var mySqlCommand4 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand4.CommandText = "SELECT mess.message_id, BIN_TO_UUID(message_uuid) as message_uuid, mess.target_username,mess.username,mess.content, mess.type, mess.deleted, DATE_FORMAT(published_at, '%Y-%m-%dT%TZ') as published_at, mess.is_read, coalesce((select approved from user_contact_approved ac where ac.username =  mess.target_username and ac.target_username = mess.username  limit 1), false) as contact_approved, (select count(*) FROM user_message messSub WHERE messSub.username = mess.username AND messSub.target_username = mess.target_username AND messSub.message_id > mess.message_id ) as additional_messages  FROM user_message mess  WHERE mess.message_id IN (" + string.Join(",", userMessageNotifications.Select(_ => "?")) + ")";
                mySqlCommand4.Parameters.AddRange(userMessageNotifications.Select(x => new MySqlParameter("", uint.Parse(x.AdditionalData.Substring(x.AdditionalData.IndexOf(":") + 1)))).ToArray());
                mySqlCommand4.Connection = connectionStory;
                using var readerMessage = mySqlCommand4.ExecuteReader();
                while (await readerMessage.ReadAsync())
                {
                    string targetUsername = readerMessage.GetString("target_username");
                    uint messageId = readerMessage.GetUInt32("message_id");
                    string messageUuid = readerMessage.GetString("message_uuid");
                    string authorUsername = readerMessage.GetString("username");
                    bool deleted = readerMessage.GetBoolean("deleted");
                    string content = readerMessage.GetString("content");
                    ushort type = readerMessage.GetUInt16("type");
                    string timestamp = readerMessage.GetString("published_at");
                    bool contactApproved = readerMessage.GetBoolean("contact_approved");
                    bool read = readerMessage.GetBoolean("is_read");
                    int additionalMessages = readerMessage.GetInt32("additional_messages");
                    var notificationToUpdate = userMessageNotifications.FirstOrDefault(x => x.AdditionalData.Contains(messageId.ToString()));
                    if (notificationToUpdate == null) continue;
                    var entry = new MessagePublishedNotificationModel(messageId, type, content, authorUsername, messageUuid, username, deleted, timestamp, contactApproved, additionalMessages, read);
                    notificationToUpdate.AdditionalData = JsonSerializer.Serialize(entry);
                }
                await readerMessage.CloseAsync();
            }
            var unreadNotifications = response.Where(x => !x.NotificationRead);
            if (unreadNotifications.Any())
            {
                var mySqlCommandUpdate = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandUpdate.CommandText = "UPDATE user_notification SET notification_read = 1 WHERE notification_id IN (" + string.Join(",", unreadNotifications.Select(_ => "?")) + ")";
                mySqlCommandUpdate.Parameters.AddRange(unreadNotifications.Select(x => new MySqlParameter("", x.NotificationId)).ToArray());
                mySqlCommandUpdate.Connection = connectionStory;
                await mySqlCommandUpdate.ExecuteNonQueryAsync();
            }
            return response;
        }
        finally
        {
            await connectionStory.CloseAsync();
            await connection.CloseAsync();
        }

    }

}