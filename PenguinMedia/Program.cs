using PenguinMedia;
using System.CommandLine;
using PenguinMedia.Audio;
using PenguinMedia.Graphic;

var inputArgument = new Argument<string>("input")
{
    Arity = ArgumentArity.ExactlyOne
};

var outputArgument = new Argument<string>("output")
{
    Arity = ArgumentArity.ExactlyOne
};

var verboseOption = new Option<bool>("--verbose")
{
    Aliases = { "-v" },
    Required = false,
};

// Audio

var audioProbeAnalyzeLoudnessOption = new Option<bool>("--analyze-loudness")
{
    Aliases = { "-l" },
    Required = false,
};

var audioProbeCommand = new Command("probe")
{
    inputArgument,
    audioProbeAnalyzeLoudnessOption,
    verboseOption,
};

audioProbeCommand.SetAction(pr =>
{
    FFmpegUtils.Initialize(pr.GetValue(verboseOption));
    var path = pr.GetRequiredValue(inputArgument);
    var analyzeLoudness = pr.GetValue(audioProbeAnalyzeLoudnessOption);

    try
    {
        var info = FFmpegUtils.Probe(path, analyzeLoudness);

        Console.Error.WriteLine(@"Audio Stream Information:");
        Console.Error.WriteLine($@"File: {path}");
        Console.Error.WriteLine($@"Stream Index: {info.StreamIndex}");
        Console.Error.WriteLine($@"Codec Name: {info.CodecName}");
        Console.Error.WriteLine($@"Sample Rate: {info.SampleRate}");
        Console.Error.WriteLine($@"Channels: {info.Channels}");
        Console.Error.WriteLine($@"Integrated Loudness: {info.IntegratedLoudness} LUFS");
        Console.Error.WriteLine($@"True Peak: {info.TruePeak} dBTP");

        Console.WriteLine(1);
    }
    catch
    {
        Console.WriteLine(0);
    }
});

var audioConvertCommand = new Command("convert")
{
    inputArgument,
    outputArgument,
};

var audioPackCommand = new Command("pack")
{
    inputArgument,
    outputArgument,
};

var audioCommand = new Command("audio")
{
    audioProbeCommand,
    audioPackCommand
};

// Graphic

var imageProbeCommand = new Command("probe")
{
    inputArgument,
};

imageProbeCommand.SetAction(pr =>
{
    var path = pr.GetRequiredValue(inputArgument);
    Console.WriteLine(ImageUtils.ProbeImage(path));
});

var imageConvertJacketCommand = new Command("convert-jacket")
{
    inputArgument,
    outputArgument,
};

imageConvertJacketCommand.SetAction(pr =>
{
    var input = pr.GetRequiredValue(inputArgument);
    var output = pr.GetRequiredValue(outputArgument);
    ImageUtils.ConvertJacket(input, output);
});

var imageConvertStageCommand = new Command("convert-stage")
{
    inputArgument,
    outputArgument,
};

var imageExtractAfbCommand = new Command("extract-afb")
{
    inputArgument,
    outputArgument,
};

imageExtractAfbCommand.SetAction(pr =>
{
    var input = pr.GetRequiredValue(inputArgument);
    var output = pr.GetRequiredValue(outputArgument);
    ImageUtils.ExtractAfb(input, output);
});

var imageCommand = new Command("image")
{
    imageProbeCommand,
    imageConvertJacketCommand,
    imageConvertStageCommand,
    imageExtractAfbCommand
};

var rootCommand = new RootCommand()
{
    audioCommand,
    imageCommand
};

var parseResult = rootCommand.Parse(args);
return parseResult.Invoke();