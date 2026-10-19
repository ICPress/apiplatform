using System.Buffers.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;
public static class ProfileImages
{
    public static string? Url(string? metadata,ServerSettings settings,bool message=false)
    {
        if(string.IsNullOrWhiteSpace(metadata))return null;
        try
        {
            var image=JsonSerializer.Deserialize<ImageInfoMetadata>(metadata);
            if(image==null||!Regex.IsMatch(image.Name,@"\A[A-Za-z0-9_-]+\.(?:jpg|jpeg|png|webp)\z",RegexOptions.IgnoreCase))return null;
            var root=message?settings.CDNRequestUserMessagePath:settings.CDNRequestLargePath;
            if(!Uri.TryCreate(root,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))return null;
            return root.TrimEnd('/')+"/"+Uri.EscapeDataString(image.Name);
        }catch(JsonException){return null;}
    }
    public static string[] MessageUrls(string content,ServerSettings settings)
    {
        try{return (JsonSerializer.Deserialize<ImageInfoMetadata[]>(content)??[]).Take(10).Select(x=>Url(JsonSerializer.Serialize(x),settings,true)).OfType<string>().ToArray();}catch(JsonException){return [];}
    }
    public static (int Width,int Height,string Extension)? Inspect(byte[] bytes)
    {
        int w=0,h=0;string extension;
        if(bytes.Length>=33&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})&&bytes.AsSpan(12,4).SequenceEqual("IHDR"u8))
        {w=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));h=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));extension=".png";}
        else if(bytes.Length>=4&&bytes[0]==255&&bytes[1]==216&&bytes[^2]==255&&bytes[^1]==217)
        {
            extension=".jpg";int p=2;
            while(p+4<=bytes.Length)
            {
                if(bytes[p++]!=255)return null;while(p<bytes.Length&&bytes[p]==255)p++;if(p>=bytes.Length)return null;var marker=bytes[p++];
                if(marker is 217 or 218)break;if(marker is 1 or >=208 and <=215)continue;
                if(p+2>bytes.Length)return null;int len=(bytes[p]<<8)|bytes[p+1];if(len<2||p+len>bytes.Length)return null;
                if(marker is 192 or 193 or 194){if(len<8)return null;h=(bytes[p+3]<<8)|bytes[p+4];w=(bytes[p+5]<<8)|bytes[p+6];break;}p+=len;
            }
        }else return null;
        return w is >0 and <=4096 && h is >0 and <=4096 ? (w,h,extension):null;
    }
}
