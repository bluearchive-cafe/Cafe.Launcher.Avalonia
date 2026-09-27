using System.Text.Json.Serialization;
using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Helpers;

namespace Cafe.Launcher.Avalonia.Models;

public sealed class LauncherApiEnvelope<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public sealed class GameConfigResponse
{
    [JsonPropertyName("game_latest_version")]
    public string? GameLatestVersion { get; set; }

    [JsonPropertyName("game_latest_file_path")]
    public string? GameLatestFilePath { get; set; }

    [JsonPropertyName("game_start_exe_name")]
    public string? GameStartExeName { get; set; }

    [JsonPropertyName("game_start_params")]
    public string[]? GameStartParams { get; set; }

    [JsonPropertyName("game_lowest_version")]
    public string? GameLowestVersion { get; set; }

    [JsonPropertyName("decompression_size")]
    public string? DecompressionSize { get; set; }
}

public sealed class BaseConfigResponse
{
    [JsonPropertyName("launcher_background_img")]
    public string? LauncherBackgroundImg { get; set; }

    [JsonPropertyName("launcher_background_img_crc64")]
    public string? LauncherBackgroundImgCrc64 { get; set; }

    [JsonPropertyName("config_open")]
    public bool ConfigOpen { get; set; }

    [JsonPropertyName("copyright_information")]
    public string? CopyrightInformation { get; set; }

    [JsonPropertyName("privacy_policy")]
    public string? PrivacyPolicy { get; set; }

    [JsonPropertyName("user_agreement")]
    public string? UserAgreement { get; set; }

    [JsonPropertyName("notice_pop_open")]
    public bool NoticePopOpen { get; set; }

    [JsonPropertyName("notice_content")]
    public string? NoticeContent { get; set; }

    [JsonPropertyName("exit_launcher_open")]
    public bool ExitLauncherOpen { get; set; }
}

public sealed class CdnConfigResponse
{
    [JsonPropertyName("primary_cdn")]
    public string? PrimaryCdn { get; set; }

    [JsonPropertyName("back_up_cdn")]
    public string? BackUpCdn { get; set; }
}

public sealed class ManifestUrlResponse
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class InstallationConfigResponse
{
    [JsonPropertyName("installer_background_img")]
    public string? InstallerBackgroundImg { get; set; }
}

public sealed class OperationsResourceResponse
{
    [JsonPropertyName("operations_resource_open")]
    public bool OperationsResourceOpen { get; set; }

    [JsonPropertyName("banner_loop")]
    public bool BannerLoop { get; set; }

    [JsonPropertyName("time_interval")]
    public int TimeInterval { get; set; }

    [JsonPropertyName("operations_banner_list")]
    public List<OperationsBannerItem> OperationsBannerList { get; set; } = [];

    [JsonPropertyName("news_list")]
    public NewsListEnvelope? NewsList { get; set; }

    [JsonPropertyName("notice_list")]
    public List<NoticeTypeItem> NoticeList { get; set; } = [];
}

public sealed class OperationsBannerItem
{
    [JsonPropertyName("banner_img")]
    public string? BannerImg { get; set; }

    [JsonPropertyName("jump_url")]
    public string? JumpUrl { get; set; }
}

public sealed class NewsListEnvelope
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public NewsListData? Data { get; set; }
}

public sealed class NewsListData
{
    [JsonPropertyName("news")]
    public List<NewsTypeItem> News { get; set; } = [];
}

public sealed class NewsTypeItem
{
    [JsonPropertyName("typeLabel")]
    public string? TypeLabel { get; set; }

    [JsonPropertyName("rows")]
    public List<NewsRowItem> Rows { get; set; } = [];
}

public sealed class NewsRowItem
{
    [JsonPropertyName("publishTime")]
    public long PublishTime { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public sealed class NoticeTypeItem
{
    [JsonPropertyName("notice_type")]
    public string? NoticeType { get; set; }

    [JsonPropertyName("notice_detail_list")]
    public List<NoticeDetailItem> NoticeDetailList { get; set; } = [];
}

public sealed class NoticeDetailItem
{
    [JsonPropertyName("notice_time")]
    public string? NoticeTime { get; set; }

    [JsonPropertyName("jump_url")]
    public string? JumpUrl { get; set; }

    [JsonPropertyName("notice_title")]
    public string? NoticeTitle { get; set; }
}

public sealed class SocialMediaResourceResponse
{
    [JsonPropertyName("social_media_resource_open")]
    public bool SocialMediaResourceOpen { get; set; }

    [JsonPropertyName("social_media_resource_list")]
    public List<SocialMediaResourceItem> SocialMediaResourceList { get; set; } = [];

    [JsonPropertyName("contact_customer_complaint")]
    public bool ContactCustomerComplaint { get; set; }

    [JsonPropertyName("contact_customer_complaint_type")]
    public int ContactCustomerComplaintType { get; set; }

    [JsonPropertyName("web_customer_complaint_url")]
    public string? WebCustomerComplaintUrl { get; set; }

    [JsonPropertyName("mail_customer_complaint_url")]
    public string? MailCustomerComplaintUrl { get; set; }

    [JsonPropertyName("aihelp_customer_complaint")]
    public AiHelpCustomerComplaint? AiHelpCustomerComplaint { get; set; }
}

public sealed class SocialMediaResourceItem
{
    [JsonPropertyName("social_media_channel")]
    public string? SocialMediaChannel { get; set; }

    [JsonPropertyName("jump_url")]
    public string? JumpUrl { get; set; }

    [JsonPropertyName("qr_img")]
    public string? QrImg { get; set; }
}

public sealed class AiHelpCustomerComplaint
{
    [JsonPropertyName("aihelp_domain")]
    public string? AihelpDomain { get; set; }

    [JsonPropertyName("aihelp_app_id")]
    public string? AihelpAppId { get; set; }

    [JsonPropertyName("aihelp_app_key")]
    public string? AihelpAppKey { get; set; }

    [JsonPropertyName("initial_interface")]
    [JsonConverter(typeof(FlexibleBoolConverter))]
    public bool InitialInterface { get; set; }
}
