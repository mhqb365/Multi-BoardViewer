# Multi BoardViewer

Ứng dụng Windows giúp xem nhiều boardview và schematic trong cùng một ứng dụng

![Multi BoardViewer](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/Platform-Windows-brightgreen) ![License](https://img.shields.io/badge/License-MIT-yellow)

![Multi BoardViewer](./Photos/1.png)
![Multi BoardViewer](./Photos/2.png)

## 🙏 Lời cảm ơn

Xin chân thành cảm ơn:
- **[BoardViewer.net](https://www.boardviewer.net/)** - phần mềm xem boardview miễn phí
- **[OpenBoardView](https://github.com/OpenBoardView)** - phần mềm xem boardview mã nguồn mở
- **[FlexBoardView](https://pldaniels.com/flexbv5/)** - thêm một phần mềm xem boardview nữa
- **[SumatraPDF](https://github.com/sumatrapdfreader)** - phần mềm đọc PDF mã nguồn mở
- **[ChinaFix - Voltage Divider Calculator](https://www.xinxunwei.com/)** - phần mềm tính toán điện áp qua cầu phân áp

Dự án này sử dụng sản phẩm của họ để tạo nên trải nghiệm xem file đa năng trong một ứng dụng duy nhất

## ✨ Tính năng

- **Multi tab**: Mở nhiều file cùng lúc
- **Multi viewer**: Xem file boardview với 3 lựa chọn viewer
- **PDF viewer**: Xem file PDF với SumatraPDF tích hợp
- **Search files**: Tìm kiếm file trong thư mục hoặc ổ đĩa chỉ định
- **Voltage Divider Calculator**: Tính toán điện áp qua cầu phân áp

## 📋 Yêu cầu hệ thống

- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## 🚀 Cài đặt và chạy

### Cách 1: Tải bản Release

1. Tải file từ [Releases](https://github.com/mhqb365/Multi-BoardViewer/releases)
2. Giải nén và chạy `MultiBoardViewer.exe`

### Cách 2: Build từ source

```powershell
# Clone repository
git clone https://github.com/mhqb365/Multi-BoardViewer.git
cd Multi-BoardViewer

# Build
.\Build.bat

# Chạy
.\Run.bat
```

## 📖 Hướng dẫn sử dụng

### Mở file

- **Tab mới**: Click nút **+** để tạo tab mới → Kéo thả file vào phần cửa sổ bên phải của ứng dụng hoặc click nút **+ Open file** và dẫn đến file cần mở
- **Search files**: Chọn thư mục hoặc ổ đĩa chứa các file tài liệu ở icon thư mục → Nhập tên file vào ô tìm kiếm → Click file và chọn viewer phù hợp để mở. Nếu mở không thành công thì đóng tab rồi mở lại với viewer khác
- **Recent files**: Danh sách các file đã mở gần đây, cũng click file để mở

### Định dạng file hỗ trợ

| Viewer | Định dạng |
|--------|-----------|
| BoardViewer | `.brd`, `.bdv`, `.fz`, `.cad`, `.tvw`, `.asc`, v.v. |
| OpenBoardView | `.brd`, `.bdv`, `.fz`, `.cad`, v.v. |
| FlexBoardView | `.brd`, `.bdv`, `.fz`, `.cad`, v.v. |
| SumatraPDF | `.pdf` |

---

## 🛠️ Development

### Công nghệ

- **Framework**: WPF + C# .NET 8.0
- **Windows API**: SetParent, MoveWindow (Process embedding)
- **External Tools**: BoardViewer, OpenBoardView, FlexBoardView, SumatraPDF, VoltageDividerCalculator

### Cấu trúc dự án

```
Multi-BoardViewer/
├── MultiBoardViewer/          # Source code chính (WPF .NET 8)
│   ├── Controls/              # User Controls (StartPage, etc.)
│   ├── Services/              # Services (FileSearch, RecentFiles)
│   ├── MainWindow.xaml        # Giao diện chính và quản lý tab
│   ├── App.xaml               # Cấu hình ứng dụng
│   └── ...
├── BoardViewer/               # Tool BoardViewer (mặc định)
├── OpenBoardView/             # Tool OpenBoardView (mã nguồn mở) 
├── FlexBoardView/             # Tool FlexBoardView
├── SumatraPDF/                # Trình xem PDF (SumatraPDF)
├── VoltageDividerCalculator/  # Công cụ tính toán điện áp
├── Photos/                    # Hình ảnh minh họa cho README
├── MultiBoardViewer.sln       # Solution file
├── Build.bat                  # Script build tự động
└── Run.bat                    # Script chạy ứng dụng nhanh
```

## 📄 License

MIT License

## Issue

FlexBoardView.exe và OpenBoardView chỉ là binary bên ngoài (không có source), và cả hai dùng SDL + OpenGL/EGL/GLES. Nếu OpenGL context tạo chậm/lỗi hoặc rơi xuống software renderer thì rất dễ bị lag/white screen. Ngược lại BoardViewer là .NET + SharpDX (DirectX), nhìn rõ ở SharpDX.Direct3D11.dll và BoardViewer.exe.config, nên thường mượt hơn.

Ngoài ra, khi mở từ app MultiBoardViewer, hai viewer này được embed dạng overlay window (MoveWindow/SetWindowLong, liên tục resize) trong MainWindow.xaml.cs. SDL/OpenGL rất nhạy với kiểu nhúng này nên có thể lag/đơ trắng, còn BoardViewer thì embed dạng child window nên ổn hơn