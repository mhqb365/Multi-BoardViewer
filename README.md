# Multi BoardViewer

Ứng dụng Windows giúp xem nhiều boardview và schematic trong cùng một ứng dụng

![Multi BoardViewer](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/Platform-Windows-brightgreen) ![License](https://img.shields.io/badge/License-MIT-yellow)

| ![Multi BoardViewer 1](./Photos/1.png) | ![Multi BoardViewer 2](./Photos/2.png) |
| :------------------------------------: | :------------------------------------: |
| ![Multi BoardViewer 3](./Photos/3.png) | ![Multi BoardViewer 4](./Photos/4.png) |

## Lời cảm ơn

Xin chân thành cảm ơn:

- **[NexusBV](https://nexusbv.net)** - phần mềm xem boardview hiện đại và mượt mà
- **[OpenBoardView](https://github.com/OpenBoardView)** - phần mềm xem boardview mã nguồn mở

- **[SumatraPDF](https://github.com/sumatrapdfreader)** - phần mềm đọc PDF mã nguồn mở

- **[Voltage Divider Tool](https://github.com/mhqb365/VoltageDividerTool)** - công cụ tính toán điện áp qua cầu phân áp

Dự án này sử dụng sản phẩm của họ để tạo nên trải nghiệm xem file đa năng trong một ứng dụng duy nhất

## Tính năng

- **Multi tab**: Mở nhiều file cùng lúc
- **Multi viewer**: Xem file boardview với 3 lựa chọn viewer (NexusBV, BoardViewer và OpenBoardView). Mặt định click vào file sẽ mở bằng NexusBV
- **PDF viewer**: Xem file PDF với SumatraPDF tích hợp
- **Voltage Divider Calculator**: Tính toán điện áp qua cầu phân áp
- **Search files**: Tìm kiếm file trong thư mục hoặc ổ đĩa chỉ định
- **Directory tree**: Xem cây thư mục của thư mục hoặc ổ đĩa chỉ định
- **Recent files**: Danh sách các file đã mở gần đây

## Yêu cầu hệ thống

- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Cài đặt và chạy

### Cách 1: Tải bản Release

1. Tải file từ [Releases](https://github.com/mhqb365/MultiBoardViewer/releases)
2. Giải nén và chạy `MultiBoardViewer.exe`

### Cách 2: Build từ source

```powershell
# Clone repository
git clone https://github.com/mhqb365/MultiBoardViewer.git
cd MultiBoardViewer

# Build
.\Build.bat

# Chạy
.\Run.bat
```

## Hướng dẫn sử dụng

### Mở file

- **Search files**: Chọn thư mục hoặc ổ đĩa chứa các file tài liệu ở icon thư mục → Nhập tên file vào ô tìm kiếm → Click file để mở bằng NexusBV, hoặc click chuột phải để mở bằng viewer khác. Nếu không mở được thì đóng tab rồi mở lại với viewer khác
- **Directory tree**: Chọn thư mục hoặc ổ đĩa chứa các file tài liệu ở icon thư mục → Click file để mở bằng NexusBV, hoặc click chuột phải để mở bằng viewer khác. Nếu không mở được thì đóng tab rồi mở lại với viewer khác
- **Recent files**: Danh sách các file đã mở gần đây
- **Drop file or Open file**: Kéo thả file vào phần cửa sổ phía dưới bên phải của ứng dụng để mở file hoặc click nút **+ Open file** và dẫn đến file cần mở
---

## Development

### Công nghệ

- **Framework**: WPF + C# .NET 8.0
- **Windows API**: SetParent, MoveWindow (Process embedding)
- **External Tools**: NexusBV, BoardViewer, OpenBoardView, SumatraPDF, Voltage Divider Tool
