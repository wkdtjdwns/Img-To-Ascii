using ImgToAscii.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImgToAscii;

public partial class MainWindow : Window
{
    // 현재 불러온 이미지 경로 (이미지 미로드 상태면 null)
    private string? _imagePath;

    // 마지막 변환 결과 (저장 & 복사 용)
    private AsciiResult? _lastResult;

    public MainWindow()
    {
        InitializeComponent();
    }

    // [이미지 열기] 버튼 — 파일 선택 다이얼로그를 열고 선택된 파일을 LoadImage로 넘김
    private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tiff|모든 파일|*.*",
            Title = "이미지 선택"
        };

        if (dlg.ShowDialog() == true) { LoadImage(dlg.FileName); }
    }

    // 드래그 중 파일이 드롭존 위에 있을 때 호출 — 파일인 경우 파란 테두리로 피드백 표시
    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZone.BorderThickness = new Thickness(2);
            DropZone.BorderBrush = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
        }

        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    // 드래그가 드롭존을 벗어날 때 테두리 원상 복구
    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZone.BorderThickness = new Thickness(0);
    }

    // 파일을 드롭존에 놓았을 때 — 첫 번째 파일만 처리 (다중 파일 드롭 무시)
    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderThickness = new Thickness(0);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
            LoadImage(files[0]);
    }

    // 이미지 파일을 불러와 미리보기에 표시하고 UI 상태를 초기화함
    private void LoadImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);

            // OnLoad: EndInit() 직후 파일 핸들을 즉시 해제 — 이후 파일 삭제/이동이 가능해짐
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            ImgPreview.Source = bitmap;
            ImgPreview.Visibility = Visibility.Visible;
            DropHint.Visibility = Visibility.Collapsed;

            _imagePath = path;
            _lastResult = null; // 이전 변환 결과 무효화

            var fi = new FileInfo(path);
            TxtImageInfo.Text = $"파일: {fi.Name}\n크기: {bitmap.PixelWidth} × {bitmap.PixelHeight} px\n용량: {fi.Length / 1024.0:F1} KB";
            TxtStatus.Text = $"불러옴: {fi.Name}";

            // 이미지 로드 후 변환 버튼만 활성화; 저장/복사는 변환 완료 후 활성화
            BtnConvert.IsEnabled = true;
            BtnSaveTxt.IsEnabled = false;
            BtnSaveHtml.IsEnabled = false;
            BtnCopy.IsEnabled = false;
            TxtAsciiResult.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이미지를 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // [변환] 버튼 — UI를 블로킹하지 않도록 Task.Run으로 변환을 백그라운드 스레드에서 실행
    private async void BtnConvert_Click(object sender, RoutedEventArgs e)
    {
        if (_imagePath is null) return;

        BtnConvert.IsEnabled = false;
        TxtStatus.Text = "변환 중...";
        TxtAsciiResult.Text = "";

        try
        {
            int width = (int)SliderWidth.Value;
            var mode = RbColor.IsChecked == true ? AsciiMode.Color : AsciiMode.Grayscale;
            string charSet = GetSelectedCharSet();

            // System.Drawing.Bitmap은 STA 스레드가 아니어도 동작하므로 Task.Run 사용 가능
            _lastResult = await Task.Run(() => AsciiConverter.Convert(_imagePath, width, mode, charSet));

            TxtAsciiResult.Text = _lastResult.Text;
            TxtStatus.Text = $"변환 완료 — {width}열 · {(mode == AsciiMode.Color ? "컬러" : "흑백")}";

            BtnSaveTxt.IsEnabled = true;
            BtnSaveHtml.IsEnabled = true;
            BtnCopy.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"변환 중 오류가 발생했습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = "변환 실패";
        }
        finally
        {
            // 예외 여부와 관계없이 이미지가 있으면 변환 버튼 복구
            BtnConvert.IsEnabled = _imagePath is not null;
        }
    }

    // [TXT 저장] — 순수 텍스트를 UTF-8로 저장 (색상 X)
    private void BtnSaveTxt_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "텍스트 파일|*.txt",
            FileName = "ascii_art.txt",
            Title = "TXT 저장"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, _lastResult.Text, System.Text.Encoding.UTF8);
            TxtStatus.Text = $"TXT 저장됨: {dlg.FileName}";
        }
    }

    // [HTML 저장] — 각 문자에 색상 스타일이 입혀진 HTML 파일로 저장 (브라우저에서 열면 컬러 아스키 아트 확인 가능)
    private void BtnSaveHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "HTML 파일|*.html",
            FileName = "ascii_art.html",
            Title = "HTML 저장"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, _lastResult.Html, System.Text.Encoding.UTF8);
            TxtStatus.Text = $"HTML 저장됨: {dlg.FileName}";
        }
    }

    // [클립보드 복사] — 텍스트만 복사 (HTML은 복사하지 않음)
    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult is null) return;
        Clipboard.SetText(_lastResult.Text);
        TxtStatus.Text = "클립보드에 복사됨";
    }

    // 너비 슬라이더 값 변경 시 옆에 숫자 레이블 갱신
    // null 체크: XAML 초기화 중 ValueChanged가 먼저 발생할 수 있음
    private void SliderWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWidthValue is null) return;
        TxtWidthValue.Text = ((int)e.NewValue).ToString();
    }

    // 폰트 크기 슬라이더 변경 시 결과 TextBox에 즉시 반영
    private void SliderFontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtAsciiResult is null || TxtFontSizeValue is null) return;
        int size = (int)e.NewValue;
        TxtFontSizeValue.Text = size.ToString();
        TxtAsciiResult.FontSize = size;
    }

    // 문자셋 콤보박스 선택 변경 이벤트 — 변환 시점에 GetSelectedCharSet()으로 읽으므로 여기서 별도 처리 불필요
    private void CmbCharset_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

    // 콤보박스 인덱스를 실제 문자셋 문자열로 변환
    // 인덱스 순서는 XAML ComboBox 항목 순서와 일치해야 함
    private string GetSelectedCharSet()
    {
        return CmbCharset.SelectedIndex switch
        {
            1 => " .:-=+*#%@",
            2 => " ░▒▓█",
            _ => " .,:;i1tfLCG08@" // 기본값 (인덱스 0 또는 미선택)
        };
    }
}
