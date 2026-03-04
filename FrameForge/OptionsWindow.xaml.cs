using System;
using System.Windows;

namespace FrameForge;

public partial class OptionsWindow : Window
{
    public OptionsWindow()
    {
        InitializeComponent();
    }

    private void AssociateExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.RegisterProjectFileAssociation();
            MessageBox.Show(
                this,
                ".ffproj 확장자 연결이 완료되었습니다.",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"확장자 연결에 실패했습니다.\n{ex.Message}",
                "옵션",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
