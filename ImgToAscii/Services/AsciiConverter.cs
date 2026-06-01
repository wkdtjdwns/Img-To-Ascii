using System.Drawing;
using System.Text;

namespace ImgToAscii.Services;

// 변환 모드: 흑백(명암만) 또는 컬러(픽셀 원본 RGB 사용)
public enum AsciiMode { Grayscale, Color }

// 변환 결과: 순수 텍스트(TXT 저장/클립보드)와 색상 HTML(브라우저 출력) 동시 보관
public record AsciiResult(string Text, string Html);

public class AsciiConverter
{
    // charSet 미지정 시 사용하는 기본 문자셋 — 어두울수록 왼쪽, 밝을수록 오른쪽
    private const string DefaultCharSet = " .,:;i1tfLCG08@";

    public static AsciiResult Convert(string imagePath, int targetWidth, AsciiMode mode, string? charSet = null)
    {
        charSet ??= DefaultCharSet;

        using var original = new Bitmap(imagePath);

        // 터미널/고정폭 폰트에서 문자 높이가 너비의 약 2.2배이므로
        // 세로 픽셀 수를 0.45배 줄여 원본 비율로 보이게 보정
        float aspectCorrection = 0.45f;
        int targetHeight = (int)(original.Height * targetWidth / (float)original.Width * aspectCorrection);
        targetHeight = Math.Max(1, targetHeight); // 최소 1행 보장

        // GDI+가 리샘플링(고품질 이중선형 보간)을 수행하므로 별도 필터 불필요
        using var resized = new Bitmap(original, new Size(targetWidth, targetHeight));

        var text = new StringBuilder();
        var html = new StringBuilder();

        // HTML 헤더: 검정 배경 + 고정폭 폰트 + 줄 간격 1.0 (행 사이 공백 제거)
        html.Append("""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8">
            <style>
            body{background:#000;margin:0;padding:16px;}
            pre{font-family:'Courier New',monospace;font-size:10px;line-height:1.0;letter-spacing:0;}
            span{white-space:pre;}
            </style>
            </head>
            <body><pre>
            """);

        for (int y = 0; y < resized.Height; y++)
        {
            for (int x = 0; x < resized.Width; x++)
            {
                var pixel = resized.GetPixel(x, y);

                // ITU-R BT.601 가중치: 인간 눈의 색상별 민감도 반영 (G > R > B)
                float brightness = (0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B) / 255f;
                char ch = BrightnessToChar(brightness, charSet);

                text.Append(ch);

                if (mode == AsciiMode.Color)
                {
                    // 컬러: 픽셀 원본 RGB를 그대로 span 색상으로 사용
                    html.Append($"<span style=\"color:#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}\">{EscapeHtml(ch)}</span>");
                }
                else
                {
                    // 흑백: 밝기값을 R=G=B로 변환해 회색조 표현
                    int gray = (int)(brightness * 255);
                    html.Append($"<span style=\"color:#{gray:X2}{gray:X2}{gray:X2}\">{EscapeHtml(ch)}</span>");
                }
            }
            text.AppendLine();
            html.AppendLine();
        }

        html.Append("</pre></body></html>");

        return new AsciiResult(text.ToString(), html.ToString());
    }

    // 밝기(0.0~1.0)를 문자셋 인덱스로 선형 매핑
    // Clamp: float 부동소수점 오차로 인한 범위 초과 방어
    private static char BrightnessToChar(float brightness, string charSet)
    {
        int index = (int)(brightness * (charSet.Length - 1));
        index = Math.Clamp(index, 0, charSet.Length - 1);
        return charSet[index];
    }

    // HTML 특수문자 이스케이프 — span 태그 안에 문자를 직접 삽입하므로 필수
    // 공백은 &nbsp;로 치환하지 않으면 HTML에서 연속 공백이 하나로 합쳐짐
    private static string EscapeHtml(char ch) => ch switch
    {
        '<' => "&lt;",
        '>' => "&gt;",
        '&' => "&amp;",
        ' ' => "&nbsp;",
        _ => ch.ToString()
    };
}
