namespace BattalionFulda;

internal sealed class MainForm : Form
{
    private readonly BattlefieldView battlefield;

    public MainForm()
    {
        Text = "Battalion: Fulda — W1 250m Battlefield";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1600, 900);
        MinimumSize = new Size(1100, 700);
        BackColor = Color.FromArgb(8, 22, 31);
        battlefield = new BattlefieldView(GameData.Load()) { Dock = DockStyle.Fill };
        Controls.Add(battlefield);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys key = keyData & Keys.KeyCode;
        return battlefield.HandleKey(key) || base.ProcessCmdKey(ref msg, keyData);
    }
}
