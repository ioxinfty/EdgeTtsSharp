namespace EdgeTtsSharp.Tests;

using System.Diagnostics;
using System.Net;
using System.Threading.Channels;

public class StreamingTests
{
    /// <summary>
    ///     在 macos 上的测试
    /// </summary>
    [Test]
    public async Task TestOnMacosAsync()
    {
        // 要播放的文章
        const string text = """
                            思念曾经的爱情

                            我想一直爱着你!痴痴的爱着你，伴你春夏与秋冬，牵你的手漫过夕阳，踩着海浪，走到尽头，从此天涯，从此海角!这不是虚假的谎言，是刻在三生石上的诺言，一念，生生世世，永不改!

                            不管世界怎么变迁，不管岁月如何流逝，对你的思念从来不曾改变。爱情的旋律，曾触动你我的的心灵，留下美好的回忆，一起追求，一样的向往，隐藏在我的内心深处，慢慢回味。

                            平静而又漫长的夜晚，你是否也会这样的想起我，有个人在为你守候，跨越时间和距离的考验，没有美丽诺言的欺骗，没有虚假的情感，如果不懂珍惜，真的会有人为你受伤。内心世界完全被你占据，就像人生的另一个转折点，加快了原本匆忙的步伐，度过孤单的日子。

                            当发现是一个遥不可及的梦，还在黄昏里痴痴的等，那些风花雪月的日子，现在除了泪水什么也没留下，脑海里不断浮现，成为回不去的记忆。夜空中下起丝丝细雨，飘舞在冰冷的脸上，和忧伤的眼泪融合在一起，滑落下地，听，是泪水破碎的声音，是心遗憾没有实现梦想的声音。

                            痛轻轻的穿越我无助的身体，进入灵魂深处不断蔓延。迷失的自己依然在做无力的挣扎，多想逃离繁华的城市，躲在一个荒芜的角落，把自己遗忘在那里。慢慢习惯一个人的世界，勉强给脸加上伪装的笑容，应该去学会坚强的。

                            所有的一切好像只是幻觉。彻底的破碎了，一切不见了。闭上疲惫的双眼，想着那份不属于自己的感情，如果没有太在乎，太执着，也不会如此迷茫，心痛流露出来的忧伤，只能用文字去代替，写下自己的过去，蓦然回首，感觉已经走了很远，很远……
                            """;


        var lines = text.Split('\r', '\n').SelectMany(it => it.Split('.', '。', '!')).Where(it => !string.IsNullOrWhiteSpace(it)).ToArray();

        // 缓存处理 3 段
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(3)
        {
            FullMode = BoundedChannelFullMode.Wait, // 如果缓存满了，生产者就等一下
        });

        EdgeTts.Proxy = new WebProxy(new Uri("http://127.0.0.1:8080"));
        var voice = await EdgeTts.GetVoice("zh-CN-XiaoxiaoNeural");

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    // 获取音频并保存到临时文件
                    await using var stream = voice.GetAudioStream(line);
                    var mp3File = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

                    await using (var fs = new FileStream(mp3File, FileMode.Create))
                    {
                        await stream.CopyToAsync(fs);
                    }

                    // 写入管道，如果管道满了（已有2段在排队），这里会异步阻塞
                    await channel.Writer.WriteAsync(mp3File);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }

            return 0;
        });

        await foreach (var mp3File in channel.Reader.ReadAllAsync())
        {
            using var process = Process.Start("afplay", mp3File);
            await process.WaitForExitAsync();

            File.Delete(mp3File);
        }
    }
}