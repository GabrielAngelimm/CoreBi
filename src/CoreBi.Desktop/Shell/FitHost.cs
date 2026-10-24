using System.Windows;
using System.Windows.Controls;

namespace CoreBi.Desktop;

/// <summary>Grid que não pede altura aos filhos: ocupa só o espaço que sobra, para a lista decidir quantas linhas cabem.</summary>
public sealed class FitHost : Grid
{
    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(new Size(constraint.Width, double.PositiveInfinity));
        return new Size(double.IsInfinity(constraint.Width) ? 0 : constraint.Width, 0);
    }

    /// <summary>Remede os filhos com a altura que o host realmente recebeu, para o histórico rolável saber que não cabe e mostrar a barra.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren) child.Measure(finalSize);
        return base.ArrangeOverride(finalSize);
    }
}
