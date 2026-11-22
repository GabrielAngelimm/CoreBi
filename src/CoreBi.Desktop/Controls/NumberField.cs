using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CoreBi.Desktop;

/// <summary>
/// Campo só de números inteiros, com faixa (estilo <c>NumberField</c> de Theme.xaml): aceita só dígitos (digitados ou colados),
/// as setas do teclado e os botões − e + do template andam um passo, presos à faixa (o botão apaga no limite), e ao sair do campo
/// o valor volta para dentro da faixa; vazio ou inválido volta ao último valor bom. Quem lê o valor continua validando, porque
/// o texto pode ser trocado em código.
/// </summary>
public static class NumberField
{
    public static readonly RoutedCommand Increase = new(nameof(Increase), typeof(NumberField));
    public static readonly RoutedCommand Decrease = new(nameof(Decrease), typeof(NumberField));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.RegisterAttached(
        "Minimum", typeof(int), typeof(NumberField), new PropertyMetadata(0, OnRangeChanged));
    public static int GetMinimum(DependencyObject d) => (int)d.GetValue(MinimumProperty);
    public static void SetMinimum(DependencyObject d, int value) => d.SetValue(MinimumProperty, value);

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(int), typeof(NumberField), new PropertyMetadata(0, OnRangeChanged));
    public static int GetMaximum(DependencyObject d) => (int)d.GetValue(MaximumProperty);
    public static void SetMaximum(DependencyObject d, int value) => d.SetValue(MaximumProperty, value);

    public static readonly DependencyProperty StepProperty = DependencyProperty.RegisterAttached(
        "Step", typeof(int), typeof(NumberField), new PropertyMetadata(1));
    public static int GetStep(DependencyObject d) => (int)d.GetValue(StepProperty);
    public static void SetStep(DependencyObject d, int value) => d.SetValue(StepProperty, value);

    // Último valor dentro da faixa: é para onde o campo volta quando sai vazio ou com algo fora do lugar
    private static readonly DependencyProperty LastGoodProperty = DependencyProperty.RegisterAttached(
        "LastGood", typeof(int?), typeof(NumberField), new PropertyMetadata(null));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(NumberField), new PropertyMetadata(false));

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || (bool)box.GetValue(HookedProperty)) return;
        box.SetValue(HookedProperty, true);
        box.PreviewTextInput += (_, args) => args.Handled = !args.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(box, OnPaste);
        box.PreviewKeyDown += OnKeyDown;
        box.TextChanged += (_, _) => Remember(box);
        box.LostKeyboardFocus += (_, _) => Normalize(box);
        box.PreviewMouseLeftButtonDown += OnPillMouseDown;
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.CommandBindings.Add(new CommandBinding(Increase, (_, _) => StepBy(box, 1), (_, a) => a.CanExecute = CanStep(box, 1)));
        box.CommandBindings.Add(new CommandBinding(Decrease, (_, _) => StepBy(box, -1), (_, a) => a.CanExecute = CanStep(box, -1)));
        Remember(box);
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !text.Trim().All(char.IsAsciiDigit)) e.CancelCommand();
    }

    // A área do texto tem só a largura do número: um clique fora dela, ainda dentro da pílula, dá o foco e seleciona o valor
    // (os botões − e + passam direto; um clique no próprio número, já com foco, posiciona o cursor como sempre)
    private static void OnPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        var box = (TextBox)sender;
        for (var node = e.OriginalSource as DependencyObject; node != null && node != box; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is System.Windows.Controls.Primitives.RepeatButton) return;
        if (box.IsKeyboardFocusWithin) return;
        box.Focus();
        box.SelectAll();
        e.Handled = true;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        switch (e.Key)
        {
            case Key.Up: StepBy(box, 1); e.Handled = true; break;
            case Key.Down: StepBy(box, -1); e.Handled = true; break;
            case Key.Space: e.Handled = true; break; // espaço não chega ao PreviewTextInput
        }
    }

    private static bool TryRead(TextBox box, out int value)
        => int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static int Clamp(TextBox box, int value) => Math.Clamp(value, GetMinimum(box), Math.Max(GetMinimum(box), GetMaximum(box)));

    private static void Remember(TextBox box)
    {
        if (TryRead(box, out int value) && value == Clamp(box, value)) box.SetValue(LastGoodProperty, value);
        CommandManager.InvalidateRequerySuggested();
    }

    private static int Current(TextBox box)
        => TryRead(box, out int value) ? Clamp(box, value) : (int?)box.GetValue(LastGoodProperty) ?? GetMinimum(box);

    private static bool CanStep(TextBox box, int direction)
    {
        int value = Current(box);
        return direction > 0 ? value < GetMaximum(box) : value > GetMinimum(box);
    }

    private static void StepBy(TextBox box, int direction)
    {
        int step = Math.Max(1, GetStep(box));
        Write(box, Clamp(box, Current(box) + direction * step));
    }

    private static void Normalize(TextBox box)
    {
        int value = Current(box);
        if (box.Text != value.ToString(CultureInfo.InvariantCulture)) Write(box, value);
    }

    private static void Write(TextBox box, int value)
    {
        box.Text = value.ToString(CultureInfo.InvariantCulture);
        box.CaretIndex = box.Text.Length;
    }
}
