using System.Windows.Data;

namespace WpfDevTools.Inspector.Analyzers;

public sealed partial class DependencyPropertyAnalyzer
{
    private static BindingBase? CreatePreviewRestoreBinding(BindingBase bindingBase) => bindingBase switch
    {
        Binding binding => CreatePreviewBinding(binding),
        MultiBinding multiBinding => CreatePreviewMultiBinding(multiBinding),
        _ => null
    };

    private static Binding CreatePreviewBinding(Binding source)
    {
        var preview = CloneBinding(source);
        preview.Mode = BindingMode.OneWay;
        preview.UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
        return preview;
    }

    private static MultiBinding CreatePreviewMultiBinding(MultiBinding source)
    {
        var preview = CloneMultiBinding(source);
        preview.Mode = BindingMode.OneWay;
        preview.UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
        return preview;
    }
}
