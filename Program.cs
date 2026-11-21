namespace NeuralRigStudio;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\NeuralRigStudio.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("NeuralRig Studio가 이미 실행 중입니다.", "NeuralRig Studio",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new MainForm());
    }
}
