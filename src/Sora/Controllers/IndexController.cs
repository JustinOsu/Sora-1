using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Sora.Enums;
using Sora.EventArgs.BanchoEventArgs;
using Sora.Framework.Enums;
using Sora.Framework.Objects;
using Sora.Framework.Utilities;
using Sora.Services;

namespace Sora.Controllers
{
    [Route("/")]
    [ApiController]
    public class IndexController : Controller
    {
        private readonly EventManager _evManager;
        private readonly PresenceService _presenceService;

        public IndexController(EventManager evManager, PresenceService presenceService)
        {
            _evManager = evManager;
            _presenceService = presenceService;
        }

        private async Task<ActionResult> RetOut(Stream stream)
        {
            stream.Position = 0;

            var m = new MemoryStream();
            await stream.CopyToAsync(m);
            m.Position = 0;

            return File(m, "application/octet-stream");
        }
        
        [HttpPost]
        public async Task<IActionResult> IndexPost([FromHeader(Name = "osu-token")] string clientToken = null)
        {
            try
            {
                Response.Headers["cho-protocol"] = "19";
                Response.Headers["Connection"] = "keep-alive";
                Response.Headers["Keep-Alive"] = "timeout=60, max=100";
                Response.Headers["cho-server"] = "Sora (https://github.com/Mempler/Sora)";

                Response.StatusCode = 200;

                using (var body = new MemoryStream()) {
                    await Request.Body.CopyToAsync(body);
                    body.Position = 0;

                    using (var mw = MStreamWriter.New())
                    using (var mr = new MStreamReader(body))
                    {
                        var pr = new Presence(new User());
                        if (string.IsNullOrEmpty(clientToken))
                        {
                            Response.Headers["cho-token"] = pr.Token.ToString();
                            string ip = Response.Headers["X-Forwarded-For"];

                            if (string.IsNullOrEmpty(ip))
                                ip = "127.0.0.1";
                            
                            await _evManager.RunEvent(EventType.BanchoLoginRequest, new BanchoLoginRequestArgs
                            {
                                Reader = mr,
                                Writer = mw,
                                pr = pr,
                                IPAddress = ip
                            });

                            mw.Flush();

                            return await RetOut(mw.BaseStream);
                        }

                        if (_presenceService.TryGet(clientToken, out pr))
                        {
                            while (true)
                                try
                                {
                                    if (Request.ContentLength - body.Position < 7)
                                        break; // Dont handle any invalid packets! (less then bytelength of 7)

                                    var packetId = (PacketId) mr.ReadInt16();
                                    mr.ReadBoolean();
                                    var packetData = mr.ReadBytes();

                                    using (var packetDataStream = new MemoryStream(packetData))
                                    using (var packetDataReader = new MStreamReader(packetDataStream))
                                    {
                                        await _evManager.RunEvent(
                                            EventType.BanchoPacket,
                                            new BanchoPacketArgs {pr = pr, PacketId = packetId, Data = packetDataReader}
                                        );
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Err(ex);
                                    break;
                                }
                            try
                            {
                                if (Response.Body.CanWrite)
                                    pr.WritePackets(Response.Body);
                                
                                return await RetOut(mw.BaseStream);
                            }
                            catch
                            {
                                // Ignored because it may throw an exception.
                            }
                        }
                        else
                            return StatusCode(403);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Err(ex);
            }

            return Ok();
        }
    }
}