using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class YoutubeData
{
    public string kind { get; set; }
    public string etag { get; set; }
    public Pageinfo pageInfo { get; set; }
    public Item[] items { get; set; }
}

public class Pageinfo
{
    public int totalResults { get; set; }
    public int resultsPerPage { get; set; }
}

public class Item
{
    public string kind { get; set; }
    public string etag { get; set; }
    public string id { get; set; }
    public Snippet snippet { get; set; }
    public Contentdetails contentDetails { get; set; }
    public Status status { get; set; }
    public Statistics statistics { get; set; }
}

public class Snippet
{
    public string publishedAt { get; set; }
    public string channelId { get; set; }
    public string title { get; set; }
    public string description { get; set; }
    public Thumbnails thumbnails { get; set; }
    public string channelTitle { get; set; }
    public string categoryId { get; set; }
    public string liveBroadcastContent { get; set; }
    public Localized localized { get; set; }
}

public class Thumbnails
{
    public Default _default { get; set; }
    public Medium medium { get; set; }
    public High high { get; set; }
    public Standard standard { get; set; }
    public Maxres maxres { get; set; }
}

public class Default
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class Medium
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class High
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class Standard
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class Maxres
{
    public string url { get; set; }
    public int width { get; set; }
    public int height { get; set; }
}

public class Localized
{
    public string title { get; set; }
    public string description { get; set; }
}

public class Contentdetails
{
    public string duration { get; set; }
    public string dimension { get; set; }
    public string definition { get; set; }
    public string caption { get; set; }
    public bool licensedContent { get; set; }
    [System.Runtime.Serialization.IgnoreDataMember]
    public string FormattedDuration
    {
        get
        {
            // Example of "duration" value PT43M42S
            if (duration.StartsWith("PT"))
            {
                try
                {
                    int hours = 0, minutes = 0, seconds = 0;
                    int startIndex = 2; // Start after "PI"
                    if (duration.IndexOf('H') > 0)
                    {
                        hours = int.Parse(duration.Substring(startIndex, duration.IndexOf('H') - startIndex));
                        startIndex = duration.IndexOf('H') + 1;
                    }
                    if (duration.IndexOf('M') > 0)
                    {
                        minutes = int.Parse(duration.Substring(startIndex, duration.IndexOf('M') - startIndex));
                        startIndex = duration.IndexOf('M') + 1;
                    }
                    if (duration.IndexOf('S') > 0)
                    {
                        seconds = int.Parse(duration.Substring(startIndex, duration.IndexOf('S') - startIndex));
                        startIndex = duration.IndexOf('S') + 1;
                    }

                    return String.Format("{0}:{1:D2}:{2:D2}", hours, minutes, seconds);
                }
                catch (Exception)
                {
                    // Fall down and return duration
                }
            }
            return duration;
        }
    }
}

public class Status
{
    public string uploadStatus { get; set; }
    public string privacyStatus { get; set; }
    public string license { get; set; }
    public bool embeddable { get; set; }
    public bool publicStatsViewable { get; set; }
}

public class Statistics
{
    public string viewCount { get; set; }
    public string likeCount { get; set; }
    public string dislikeCount { get; set; }
    public string favoriteCount { get; set; }
    public string commentCount { get; set; }
}
