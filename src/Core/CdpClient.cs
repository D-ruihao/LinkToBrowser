using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LinkToBrowser.Core
{
    public static class CdpClient
    {
        private const string ActivePortFileName = "DevToolsActivePort";

        public static bool HasDevToolsActivePort(string userDataDir)
        {
            if (string.IsNullOrWhiteSpace(userDataDir))
            {
                return false;
            }

            try
            {
                return File.Exists(Path.Combine(userDataDir, ActivePortFileName));
            }
            catch
            {
                return false;
            }
        }

        public static bool TryParseDevToolsActivePort(string filePath, out int port, out string wsPath)
        {
            port = 0;
            wsPath = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            try
            {
                var lines = File.ReadAllLines(filePath);
                if (lines.Length < 2)
                {
                    return false;
                }

                if (!int.TryParse(lines[0].Trim(), out port) || port <= 0 || port > 65535)
                {
                    return false;
                }

                wsPath = lines[1].Trim();
                if (string.IsNullOrEmpty(wsPath) || !wsPath.StartsWith("/", StringComparison.Ordinal))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string BuildCreateTargetPayload(int id, string url)
        {
            var payload = new Dictionary<string, object>
            {
                ["id"] = id,
                ["method"] = "Target.createTarget",
                ["params"] = new Dictionary<string, object>
                {
                    ["url"] = url ?? string.Empty
                }
            };
            return new JavaScriptSerializer().Serialize(payload);
        }

        public static bool TryValidateCreateTargetResponse(string responseJson, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                error = "Empty CDP response.";
                return false;
            }

            try
            {
                var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(responseJson);
                if (parsed == null)
                {
                    error = "Invalid CDP JSON response.";
                    return false;
                }

                if (parsed.TryGetValue("error", out var errObj) && errObj != null)
                {
                    error = errObj is Dictionary<string, object> errDict &&
                            errDict.TryGetValue("message", out var msg)
                        ? Convert.ToString(msg)
                        : Convert.ToString(errObj);
                    return false;
                }

                if (parsed.TryGetValue("result", out var resObj) &&
                    resObj is Dictionary<string, object> resDict &&
                    resDict.TryGetValue("targetId", out var targetId) &&
                    !string.IsNullOrWhiteSpace(Convert.ToString(targetId)))
                {
                    return true;
                }

                error = "CDP response missing targetId.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryOpenTab(string userDataDir, string url, out string error, int timeoutMs = 2500)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(userDataDir))
            {
                error = "User data directory is empty.";
                return false;
            }

            var portFile = Path.Combine(userDataDir, ActivePortFileName);
            if (!TryParseDevToolsActivePort(portFile, out var port, out var wsPath))
            {
                error = "DevToolsActivePort not found or invalid.";
                return false;
            }

            try
            {
                var uri = new Uri($"ws://127.0.0.1:{port}{wsPath}");
                var result = Task.Run(() => OpenTabAsync(uri, url, timeoutMs)).GetAwaiter().GetResult();
                error = result.err;
                return result.ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static async Task<(bool ok, string err)> OpenTabAsync(Uri wsUri, string url, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var ws = new ClientWebSocket();

            try
            {
                await ws.ConnectAsync(wsUri, cts.Token).ConfigureAwait(false);

                var json = BuildCreateTargetPayload(1, url);
                var sendBytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

                var buffer = new byte[8192];
                using var ms = new MemoryStream();
                var serializer = new JavaScriptSerializer();
                while (!cts.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(buffer);
                    var recv = await ws.ReceiveAsync(segment, cts.Token).ConfigureAwait(false);
                    if (recv.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    ms.Write(buffer, 0, recv.Count);
                    if (recv.EndOfMessage)
                    {
                        var text = Encoding.UTF8.GetString(ms.ToArray());
                        ms.SetLength(0);

                        Dictionary<string, object> msg = null;
                        try
                        {
                            msg = serializer.Deserialize<Dictionary<string, object>>(text);
                        }
                        catch
                        {
                        }

                        if (msg != null &&
                            msg.TryGetValue("id", out var idVal) &&
                            int.TryParse(Convert.ToString(idVal), out var replyId) &&
                            replyId == 1)
                        {
                            var valid = TryValidateCreateTargetResponse(text, out var err);
                            try
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
                            }
                            catch
                            {
                            }

                            return (valid, err);
                        }
                    }
                }

                return (false, "Timed out waiting for CDP Target.createTarget response.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
