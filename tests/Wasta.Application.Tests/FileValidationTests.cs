using Wasta.Application.Features.Files;

namespace Wasta.Application.Tests;

public class FileValidationTests
{
    private static readonly byte[] PdfHeader = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04];

    // "MZ" - a Windows executable.
    private static readonly byte[] ExecutableHeader = [0x4D, 0x5A, 0x90, 0x00];

    [Fact]
    public void A_real_pdf_is_accepted_as_a_cv()
    {
        var result = FileValidation.Validate(
            FileKind.Cv, "layla-cv.pdf", "application/pdf", 1024, PdfHeader);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_executable_renamed_to_pdf_is_rejected()
    {
        // The filename says pdf and the header says pdf. Only the bytes say
        // otherwise, which is exactly why the bytes are what gets checked.
        var result = FileValidation.Validate(
            FileKind.Cv, "cv.pdf", "application/pdf", 4096, ExecutableHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.content_mismatch", result.Code);
    }

    [Fact]
    public void A_png_sent_as_a_pdf_is_rejected()
    {
        var result = FileValidation.Validate(
            FileKind.Cv, "cv.pdf", "application/pdf", 2048, PngHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.content_mismatch", result.Code);
    }

    [Fact]
    public void A_cv_over_five_megabytes_is_rejected()
    {
        var result = FileValidation.Validate(
            FileKind.Cv, "cv.pdf", "application/pdf", FileValidation.MaxCvBytes + 1, PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.too_large", result.Code);
    }

    [Fact]
    public void A_cv_must_be_a_pdf_and_nothing_else()
    {
        var result = FileValidation.Validate(
            FileKind.Cv, "cv.png", "image/png", 1024, PngHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.type_not_allowed", result.Code);
    }

    [Fact]
    public void A_png_is_fine_as_a_project_attachment()
    {
        var result = FileValidation.Validate(
            FileKind.ProjectAttachment, "screenshot.png", "image/png", 1024, PngHeader);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_office_document_is_accepted_on_its_zip_signature()
    {
        var result = FileValidation.Validate(
            FileKind.ProjectAttachment,
            "deck.pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            2048,
            ZipHeader);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_empty_file_is_rejected()
    {
        var result = FileValidation.Validate(FileKind.Cv, "cv.pdf", "application/pdf", 0, PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.empty", result.Code);
    }

    [Fact]
    public void A_missing_content_type_is_rejected()
    {
        var result = FileValidation.Validate(FileKind.Cv, "cv.pdf", null, 1024, PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal("file.type_not_allowed", result.Code);
    }

    [Fact]
    public void A_content_type_with_a_charset_parameter_still_matches()
    {
        var result = FileValidation.Validate(
            FileKind.Cv, "cv.pdf", "application/pdf; charset=binary", 1024, PdfHeader);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config", "config")]
    [InlineData("/absolute/path/cv.pdf", "cv.pdf")]
    [InlineData("...", "upload")]
    [InlineData("", "upload")]
    public void Path_segments_are_stripped_from_a_filename(string input, string expected)
    {
        // The name is echoed back on download, so a traversal sequence or a
        // leading dot has to be flattened before it goes anywhere near a header.
        Assert.Equal(expected, FileValidation.SanitiseFileName(input));
    }

    [Fact]
    public void A_very_long_filename_is_truncated()
    {
        var name = new string('a', 500) + ".pdf";

        Assert.Equal(200, FileValidation.SanitiseFileName(name).Length);
    }
}
