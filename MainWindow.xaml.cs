using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Documents;

namespace PaperEraser;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private byte[]? original;
    private BitmapSource? result;
    private string originalName = "试卷";
    private Point selectionStart;
    private Int32Rect? selection;
    private bool selecting;
    private Point collectionDragStart;
    private string? collectionDragCandidate;
    private bool collectionDragInProgress;
    private CollectionDropAdorner? collectionDropMarker;
    private AdornerLayer? collectionDropLayer;
    private Point dragStart;
    private QuestionItem? dragQuestion;
    private QuestionDropAdorner? dropMarker;
    private AdornerLayer? dropMarkerLayer;
    private bool isBusy;
    private double outputZoom = 1.0;
    private readonly ObservableCollection<Worksheet> pages = new();
    private Worksheet? currentPage;
    private bool stopBatch;
    private bool sessionWritable = true;
    private bool loadingSession;
    private string sessionHash = "";
    private readonly ObservableCollection<QuestionItem> questions = new();
    private bool basketStorageAvailable = true;
    private ICollectionView? questionView;
    private LibraryConfig library = new();
    private bool libraryWritable = true;
    private bool suppressBasketSave;
    private string basketHash = "";
    private string selectedCollection = "全部错题";
    private string selectedSubject = "全部学科";
    public MainWindow()
    {
        InitializeComponent();
        PageList.ItemsSource = pages;
        RestoreSession();
        Closing += (_, e) =>
        {
            if (!sessionWritable && pages.Any(p => p.Regions.Count > 0) && MessageBox.Show("试卷进度自动保存不可用，未加入错题集的选区会丢失。确认关闭吗？", "关闭程序", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
            if (isBusy && !e.Cancel && MessageBox.Show("批量处理仍在进行，关闭可能中断请求且仍可能消耗额度。确认关闭吗？", "处理进行中", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) e.Cancel = true;
        };
        questionView = CollectionViewSource.GetDefaultView(questions);
        questionView.Filter = QuestionMatches;
        QuestionList.ItemsSource = questionView;
        try { library = LibraryStore.Load(); }
        catch (Exception ex) { libraryWritable = false; MessageBox.Show($"无法读取学科和错题集配置，已暂停修改配置以保护原文件。\n{LibraryStore.PathName}\n\n{ex.Message}", "读取失败"); }
        try
        {
            foreach (var item in BasketStore.Load(BasketStore.DefaultPath)) questions.Add(item);
            basketHash = BasketFingerprint();
            questions.CollectionChanged += (_, _) => SaveBasket();
            if (questions.Count > 0) Status.Text = $"已恢复本机错题篮，共 {questions.Count} 题。";
            SyncLibrary();
            RefreshLibraryControls();
        }
        catch (Exception ex)
        {
            basketStorageAvailable = false;
            MessageBox.Show($"错题篮读取失败，已暂停自动保存以保护原文件。请先备份该文件并检查：\n{BasketStore.DefaultPath}\n\n{ex.Message}", "无法恢复错题篮", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        DataPathText.Text = System.IO.Path.GetDirectoryName(BasketStore.DefaultPath);
        SetPage("library");
        try { var stored = CredentialStore.Load(); if (stored is { } c) { ClientId.Text = c.Id; ClientSecret.Password = c.Secret; Status.Text = $"已载入 API 凭据；本机错题篮共 {questions.Count} 题。"; } }
        catch (Exception ex) { Status.Text = "无法读取保存的凭据：" + ex.Message; }
    }

    private void RestoreSession()
    {
        loadingSession = true;
        try
        {
            var restored = WorksheetStore.Load(); sessionHash = WorksheetStore.Fingerprint();
            foreach (var page in restored.Pages) { page.PropertyChanged += SessionPageChanged; pages.Add(page); }
            PageList.SelectedItem = pages.FirstOrDefault(p => p.Id == restored.Selected) ?? pages.FirstOrDefault();
            pages.CollectionChanged += SessionPagesChanged;
        }
        catch (Exception ex)
        {
            sessionWritable = false; pages.CollectionChanged += SessionPagesChanged;
            MessageBox.Show($"试卷进度读取失败，已暂停自动保存以保护原文件。\n{WorksheetStore.DirectoryPath}\n\n{ex.Message}", "无法恢复进度", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { loadingSession = false; }
    }
    private void SessionPagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (Worksheet page in e.OldItems) page.PropertyChanged -= SessionPageChanged;
        if (e.NewItems is not null) foreach (Worksheet page in e.NewItems) page.PropertyChanged += SessionPageChanged;
        SaveSession();
    }
    private void SessionPageChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Worksheet.State)) SaveSession(); }
    private void SaveSession()
    {
        if (!sessionWritable || loadingSession) return;
        try
        {
            if (WorksheetStore.Fingerprint() != sessionHash) throw new IOException("试卷进度已被其他窗口修改，请关闭其他版本后重新打开。");
            WorksheetStore.Save(pages.ToArray(), currentPage?.Id); sessionHash = WorksheetStore.Fingerprint();
        }
        catch (Exception ex)
        {
            sessionWritable = false;
            MessageBox.Show($"试卷进度自动保存失败，本次新增更改只在内存中，请保存处理结果并加入错题集。\n\n{ex.Message}", "自动保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenUsage_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "https://scan.quark.cn/business/center?entry=allapis", UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开夸克后台"); }
    }

    private void SetPage(string page)
    {
        LibraryPage.Visibility = page == "library" ? Visibility.Visible : Visibility.Collapsed;
        ProcessPage.Visibility = page == "process" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        var active = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 235, 254));
        var clear = System.Windows.Media.Brushes.Transparent;
        LibraryNav.Background = page == "library" ? active : clear; ProcessNav.Background = page == "process" ? active : clear;
        SettingsNav.Background = page == "settings" ? active : clear;
    }
    private void NavLibrary_Click(object sender, RoutedEventArgs e) => SetPage("library");
    private void NavProcess_Click(object sender, RoutedEventArgs e) => SetPage("process");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => SetPage("settings");
    private void ImportFromHome_Click(object sender, RoutedEventArgs e) { SetPage("process"); Open_Click(sender, e); }
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { var dir = Path.GetDirectoryName(BasketStore.DefaultPath)!; Directory.CreateDirectory(dir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开文件夹"); }
    }
    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!basketStorageAvailable || !libraryWritable) { MessageBox.Show("当前数据未能完整读取或保存，暂不能备份。", "无法备份"); return; }
        var dialog = new SaveFileDialog { Filter = "PaperEraser 备份|*.zip", DefaultExt = ".zip", FileName = $"错题备份_{DateTime.Now:yyyyMMdd}.zip" };
        if (dialog.ShowDialog() != true) return;
        try { EnsureBasketUnchanged(); BackupService.Create(dialog.FileName, questions.ToArray(), library); Status.Text = "错题数据已备份：" + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "备份失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!basketStorageAvailable || !libraryWritable) { MessageBox.Show("当前数据不可写，请先解决读取或保存问题。", "无法恢复"); return; }
        var dialog = new OpenFileDialog { Filter = "PaperEraser 备份|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var data = BackupService.Read(dialog.FileName);
            if (MessageBox.Show($"备份包含 {data.Questions.Count} 道错题。恢复将替换当前错题、学科和错题集；现有数据会先保存一份恢复前备份。继续吗？", "确认恢复", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            EnsureBasketUnchanged();
            var backupPath = BackupService.Restore(data);
            basketHash = BasketFingerprint(); library = data.Library;
            suppressBasketSave = true;
            try { questions.Clear(); foreach (var item in data.Questions) questions.Add(item); }
            finally { suppressBasketSave = false; }
            selectedCollection = "全部错题"; selectedSubject = "全部学科"; SyncLibrary(); RefreshLibraryControls();
            Status.Text = $"已恢复 {questions.Count} 道错题。恢复前备份：{backupPath}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "恢复失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void SyncLibrary()
    {
        if (library.Collections.Count == 0) library.Collections.Add("默认错题集");
        foreach (var name in questions.Select(q => q.Collection).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()) if (!library.Collections.Contains(name)) library.Collections.Add(name);
        foreach (var name in questions.Select(q => q.Subject).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()) if (!library.Subjects.Contains(name)) library.Subjects.Add(name);
    }
    private void RefreshLibraryControls()
    {
        if (!library.Collections.Contains(selectedCollection) && selectedCollection != "全部错题") selectedCollection = "全部错题";
        if (!library.Subjects.Contains(selectedSubject) && selectedSubject != "全部学科") selectedSubject = "全部学科";
        CollectionList.ItemsSource = new[] { "全部错题" }.Concat(library.Collections).ToArray(); CollectionList.SelectedItem = selectedCollection;
        SubjectFilter.ItemsSource = new[] { "全部学科" }.Concat(library.Subjects).ToArray(); SubjectFilter.SelectedItem = selectedSubject;
        var target = TargetCollection.SelectedItem as string;
        TargetCollection.ItemsSource = library.Collections.ToArray(); TargetCollection.SelectedItem = library.Collections.Contains(target ?? "") ? target : library.Collections.FirstOrDefault();
        var subjectTarget = TargetSubject.SelectedItem as string;
        TargetSubject.ItemsSource = new[] { "未分类" }.Concat(library.Subjects).ToArray();
        TargetSubject.SelectedItem = subjectTarget is not null && (subjectTarget == "未分类" || library.Subjects.Contains(subjectTarget)) ? subjectTarget : "未分类";
        CollectionTitle.Text = selectedCollection; questionView?.Refresh();
    }
    private void CollectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CollectionList?.SelectedItem is not string value) return;
        selectedCollection = value; if (CollectionTitle is not null) CollectionTitle.Text = value;
        if (value != "全部错题" && TargetCollection is not null) TargetCollection.SelectedItem = value;
        questionView?.Refresh();
    }
    private ListBoxItem? CollectionContainer(DependencyObject? source) => source is null ? null : ItemsControl.ContainerFromElement(CollectionList, source) as ListBoxItem;
    private void CollectionList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        collectionDragStart = e.GetPosition(CollectionList);
        collectionDragCandidate = CollectionContainer(e.OriginalSource as DependencyObject)?.DataContext as string;
        if (collectionDragCandidate == "全部错题") collectionDragCandidate = null;
    }
    private void CollectionList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || collectionDragCandidate is null || !libraryWritable) return;
        var point = e.GetPosition(CollectionList);
        if (!collectionDragInProgress)
        {
            if (Math.Abs(point.X - collectionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - collectionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!CollectionList.CaptureMouse()) { collectionDragCandidate = null; return; }
            collectionDragInProgress = true;
        }
        var target = CollectionAtPoint(point);
        if (target?.DataContext is not string targetName || targetName == collectionDragCandidate)
        { ClearCollectionDropMarker(); return; }
        bool before = targetName != "全部错题" && e.GetPosition(target).Y < target.ActualHeight / 2;
        if (collectionDropMarker?.AdornedElement != target || collectionDropMarker.Before != before)
        {
            ClearCollectionDropMarker(); var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer is not null) { collectionDropMarker = new CollectionDropAdorner(target, before); collectionDropLayer = layer; layer.Add(collectionDropMarker); }
        }
        e.Handled = true;
    }
    private ListBoxItem? CollectionAtPoint(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > CollectionList.ActualWidth || point.Y > CollectionList.ActualHeight) return null;
        return CollectionContainer(CollectionList.InputHitTest(point) as DependencyObject);
    }
    private void ClearCollectionDropMarker()
    {
        if (collectionDropMarker is not null && collectionDropLayer is not null) collectionDropLayer.Remove(collectionDropMarker);
        collectionDropMarker = null; collectionDropLayer = null;
    }
    private void EndCollectionDrag()
    {
        collectionDragInProgress = false; collectionDragCandidate = null; ClearCollectionDropMarker();
        if (CollectionList.IsMouseCaptured) CollectionList.ReleaseMouseCapture();
    }
    private void CollectionList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!collectionDragInProgress) { collectionDragCandidate = null; return; }
        var source = collectionDragCandidate; var container = CollectionAtPoint(e.GetPosition(CollectionList));
        var target = container?.DataContext as string;
        bool before = target != "全部错题" && container is not null && e.GetPosition(container).Y < container.ActualHeight / 2;
        EndCollectionDrag();
        if (source is not null && target is not null && source != target) ReorderCollections(source, target, before);
        e.Handled = true;
    }
    private void CollectionList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (collectionDragInProgress) EndCollectionDrag();
    }
    private void CollectionList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && collectionDragInProgress) { EndCollectionDrag(); e.Handled = true; }
    }
    private bool ReorderCollections(string source, string target, bool before)
    {
        if (!libraryWritable || source == "全部错题") return false;
        int from = library.Collections.IndexOf(source);
        int targetIndex = target == "全部错题" ? -1 : library.Collections.IndexOf(target);
        if (from < 0 || (targetIndex < 0 && target != "全部错题") || source == target) return false;
        int destination = target == "全部错题" ? 0 : CollectionDropAdorner.DestinationIndex(from, targetIndex, before);
        if (destination == from) return false;
        var old = library.Collections.ToList(); library.Collections.RemoveAt(from); library.Collections.Insert(destination, source);
        if (!SaveLibrary()) { library.Collections = old; return false; }
        RefreshLibraryControls(); Status.Text = "错题集顺序已更新并保存。"; return true;
    }
    private void SubjectFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SubjectFilter?.SelectedItem is not string value) return; selectedSubject = value; questionView?.Refresh();
    }
    private bool SaveLibrary()
    {
        if (!libraryWritable) { MessageBox.Show("学科和错题集配置不可写，请先修复配置文件。", "保存失败"); return false; }
        try { LibraryStore.Save(library); return true; }
        catch (Exception ex) { MessageBox.Show("无法保存配置：" + ex.Message, "保存失败"); return false; }
    }
    private bool CommitQuestions(IReadOnlyList<QuestionItem> updated)
    {
        if (!basketStorageAvailable) { MessageBox.Show("错题篮不可写，无法保存修改。", "保存失败"); return false; }
        try { EnsureBasketUnchanged(); BasketStore.Save(BasketStore.DefaultPath, updated); basketHash = BasketFingerprint(); }
        catch (Exception ex) { MessageBox.Show("无法保存错题：" + ex.Message, "保存失败"); return false; }
        suppressBasketSave = true;
        try { questions.Clear(); foreach (var item in updated) questions.Add(item); }
        finally { suppressBasketSave = false; }
        return true;
    }
    private void NewCollection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NameDialog("新建错题集", "错题集名称") { Owner = this }; if (dialog.ShowDialog() != true) return;
        var name = dialog.Value; if (name == "全部错题" || library.Collections.Contains(name)) { MessageBox.Show("名称已存在，请使用其他名称。"); return; }
        library.Collections.Add(name); if (!SaveLibrary()) { library.Collections.Remove(name); return; }
        selectedCollection = name; RefreshLibraryControls(); TargetCollection.SelectedItem = name;
    }
    private void RenameCollection_Click(object sender, RoutedEventArgs e)
    {
        var old = selectedCollection;
        if (old == "全部错题") { MessageBox.Show("请先选择一个错题集。", "重命名错题集"); return; }
        var dialog = new NameDialog("重命名错题集", "新的错题集名称", old) { Owner = this }; if (dialog.ShowDialog() != true || dialog.Value == old) return;
        var name = dialog.Value; if (name == "全部错题" || library.Collections.Contains(name)) { MessageBox.Show("名称已存在，请使用其他名称。"); return; }
        if (!libraryWritable) { MessageBox.Show("配置不可写，无法重命名。"); return; }
        var updated = questions.Select(q => q.Collection == old ? q with { Collection = name } : q).ToArray();
        if (!CommitQuestions(updated)) return;
        library.Collections[library.Collections.IndexOf(old)] = name; selectedCollection = name; SaveLibrary(); RefreshLibraryControls(); TargetCollection.SelectedItem = name;
    }
    private void ManageSubjects_Click(object sender, RoutedEventArgs e)
    {
        var win = new Window { Title = "管理学科", Owner = this, Width = 470, Height = 500, MinWidth = 400, MinHeight = 360 };
        UiFactory.Configure(win);
        var root = new DockPanel { Margin = new Thickness(22) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        header.Children.Add(new TextBlock { Text = "管理学科", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var hint = UiFactory.Hint("管理错题的学科分类。选中学科后可重命名或删除。"); hint.Margin = new Thickness(0, 6, 0, 0); header.Children.Add(hint);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(row, Dock.Bottom); root.Children.Add(row);
        var list = new ListBox { ItemsSource = library.Subjects.ToArray(), BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, ItemContainerStyle = (Style)FindResource("CollectionItem") };
        root.Children.Add(UiFactory.Card(list));
        void Reload() { list.ItemsSource = library.Subjects.ToArray(); RefreshLibraryControls(); }
        var add = new Button { Content = "＋ 添加学科" }; UiFactory.Primary(add);
        var rename = new Button { Content = "重命名" }; var remove = new Button { Content = "删除" };
        row.Children.Add(add); row.Children.Add(rename); row.Children.Add(remove); win.Content = root;
        add.Click += (_, _) => { var d = new NameDialog("添加学科", "学科名称") { Owner = win }; if (d.ShowDialog() != true) return; if (d.Value == "全部学科" || library.Subjects.Contains(d.Value)) { MessageBox.Show("名称已存在。"); return; } library.Subjects.Add(d.Value); if (!SaveLibrary()) library.Subjects.Remove(d.Value); Reload(); };
        rename.Click += (_, _) =>
        {
            if (list.SelectedItem is not string old) return;
            var d = new NameDialog("重命名学科", "新的学科名称", old) { Owner = win }; if (d.ShowDialog() != true || d.Value == old) return;
            if (d.Value == "全部学科" || library.Subjects.Contains(d.Value)) { MessageBox.Show("名称已存在。"); return; }
            if (!libraryWritable || !CommitQuestions(questions.Select(q => q.Subject == old ? q with { Subject = d.Value } : q).ToArray())) return;
            library.Subjects[library.Subjects.IndexOf(old)] = d.Value; if (selectedSubject == old) selectedSubject = d.Value; SaveLibrary(); Reload();
        };
        remove.Click += (_, _) => { if (list.SelectedItem is not string name) return; if (questions.Any(q => q.Subject == name)) { MessageBox.Show("该学科仍有错题，请先修改这些题目的学科。", "无法删除"); return; } var index = library.Subjects.IndexOf(name); library.Subjects.RemoveAt(index); if (!SaveLibrary()) library.Subjects.Insert(index, name); Reload(); };
        win.ShowDialog();
    }

    private static string BasketFingerprint() => File.Exists(BasketStore.DefaultPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(BasketStore.DefaultPath))) : "(missing)";
    private void EnsureBasketUnchanged()
    {
        if (BasketFingerprint() != basketHash) throw new IOException("错题数据已被其他程序或窗口修改。为避免覆盖，请关闭其他版本并重新打开本程序。");
    }
    private void SaveBasket()
    {
        if (!basketStorageAvailable || suppressBasketSave) return;
        try { EnsureBasketUnchanged(); BasketStore.Save(BasketStore.DefaultPath, questions); basketHash = BasketFingerprint(); }
        catch (Exception ex)
        {
            basketStorageAvailable = false;
            MessageBox.Show($"错题篮自动保存失败，当前更改仅在内存中。请先导出 PDF：\n{ex.Message}\n\n保存位置：{BasketStore.DefaultPath}", "自动保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool SaveCredentials()
    {
        var id = ClientId.Text.Trim(); var secret = ClientSecret.Password;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) { MessageBox.Show("请填写 Client ID 和 Client Secret。", "缺少凭据"); return false; }
        try { CredentialStore.Save(id, secret); Status.Text = "API 凭据已保存到当前 Windows 用户的凭据管理器。"; return true; }
        catch (Exception ex) { MessageBox.Show("无法保存凭据：" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }
    private void SaveCredentials_Click(object sender, RoutedEventArgs e) { if (SaveCredentials()) RememberCredentials.IsChecked = true; }
    private void ClearCredentials_Click(object sender, RoutedEventArgs e)
    {
        try { CredentialStore.Delete(); ClientId.Clear(); ClientSecret.Clear(); RememberCredentials.IsChecked = false; Status.Text = "已清除保存的凭据和当前输入。"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "清除失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }


    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        ImportFiles(dialog.FileNames);
    }
    private List<Worksheet> ImportFiles(IEnumerable<string> paths)
    {
        var added = new List<Worksheet>(); var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var info = new FileInfo(path); if (info.Length > 10 * 1024 * 1024) throw new Exception("超过 10 MB");
                added.Add(ImportBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path)));
            }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
        }
        if (errors.Count > 0) MessageBox.Show($"已导入 {added.Count} 张，以下图片未导入：\n{string.Join("\n", errors.Take(8))}", "部分图片无法导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        return added;
    }
    private void ImportFile(string path) => ImportFiles(new[] { path });
    private Worksheet ImportBytes(byte[] bytes, string name)
    {
        var normalized = ValidateImage(bytes);
        var page = new Worksheet(name, normalized.Bytes, normalized.Image); pages.Add(page); PageList.SelectedItem = page; SetPage("process");
        Status.Text = normalized.Corrected
            ? $"已导入 {name} 并按照片方向自动转正；当前共有 {pages.Count} 页。"
            : $"已导入 {name}；当前共有 {pages.Count} 页。导入不会消耗 API 额度。";
        return page;
    }
    private void PageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        currentPage = PageList.SelectedItem as Worksheet; ShowCurrentPage(); SaveSession();
    }
    private void ShowCurrentPage()
    {
        if (OriginalImage is null || ResultImage is null) return;
        ResetSelection(); original = currentPage?.OriginalBytes; originalName = currentPage?.Name ?? "试卷";
        OriginalImage.Source = currentPage?.Original; result = currentPage?.Processed; ResultImage.Source = result;
        ResultSurface.Width = result?.PixelWidth ?? 1; ResultSurface.Height = result?.PixelHeight ?? 1;
        EraseButton.IsEnabled = !isBusy && original is not null; SaveButton.IsEnabled = result is not null; ExportPageButton.IsEnabled = result is not null;
        RemovePageButton.IsEnabled = !isBusy && currentPage is not null;
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = !isBusy && currentPage is not null; RefreshRegions();
    }
    private void RemovePage_Click(object sender, RoutedEventArgs e)
    {
        if (currentPage is null) return;
        if (currentPage.Regions.Count > 0 && MessageBox.Show("当前页还有未加入错题集的选区，移除页面会丢失这些选区。继续吗？", "移除页面", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var index = PageList.SelectedIndex; pages.Remove(currentPage);
        if (pages.Count > 0) PageList.SelectedIndex = Math.Min(index, pages.Count - 1); else { currentPage = null; ShowCurrentPage(); }
    }
    private static (BitmapSource Image, byte[] Bytes, bool Corrected) ValidateImage(byte[] bytes)
    {
        if (bytes.Length > 10 * 1024 * 1024) throw new Exception("图片超过 10 MB，请先压缩后再导入。");
        var normalized = ImageOrientation.DecodeAndNormalize(bytes);
        if (normalized.Image.PixelWidth <= 15 || normalized.Image.PixelHeight <= 15 || normalized.Image.PixelWidth >= 8192 || normalized.Image.PixelHeight >= 8192 || Math.Max(normalized.Image.PixelWidth, normalized.Image.PixelHeight) / (double)Math.Min(normalized.Image.PixelWidth, normalized.Image.PixelHeight) >= 50)
            throw new Exception("图片尺寸不符合要求：宽高均需大于 15、小于 8192 像素，长宽比小于 50。");
        if (normalized.Bytes.Length > 10 * 1024 * 1024) throw new Exception("图片转正后超过 10 MB，请先压缩后再导入。");
        return normalized;
    }
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => RotateCurrentPage(false);
    private void RotateRight_Click(object sender, RoutedEventArgs e) => RotateCurrentPage(true);
    private void RotateCurrentPage(bool clockwise)
    {
        if (currentPage is null || isBusy) return;
        ResetSelection(); currentPage.Rotate(clockwise); ShowCurrentPage(); SaveSession();
        Status.Text = clockwise ? "当前页已向右旋转 90°。" : "当前页已向左旋转 90°。";
    }
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(QuestionItem))) return;
        e.Effects = OpenButton.IsEnabled && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!OpenButton.IsEnabled) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            ImportFiles(files);
        }
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase && Keyboard.FocusedElement is not System.Windows.Controls.PasswordBox && e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase && e.OriginalSource is not System.Windows.Controls.PasswordBox) { PasteImage(); e.Handled = true; }
    }
    private void Paste_Click(object sender, RoutedEventArgs e) => PasteImage();
    private void PasteImage()
    {
        if (!OpenButton.IsEnabled) return;
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                ImportFiles(files.Cast<string>()); return;
            }
            if (!Clipboard.ContainsImage()) { MessageBox.Show("剪贴板中没有图片。请先复制图片文件或截图。", "提示"); return; }
            var bitmap = Clipboard.GetImage() ?? throw new Exception("无法读取剪贴板图片。");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); ImportBytes(stream.ToArray(), "粘贴的试卷");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法粘贴图片", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Erase_Click(object sender, RoutedEventArgs e)
    {
        if (original is null) return;
        if (result is not null && MessageBox.Show("当前图片已有处理结果。重新处理会再次调用接口，可能消耗额度，是否继续？", "确认重新处理", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var id = ClientId.Text.Trim(); var secret = ClientSecret.Password;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) { SetPage("settings"); MessageBox.Show("请先在设置中填写开放平台 API 的 Client ID 和 Client Secret。", "缺少凭据"); return; }
        if (RememberCredentials.IsChecked == true && !SaveCredentials()) return;
        SetBusy(true);
        Status.Text = "正在上传并处理，请稍候。请求可能消耗接口额度……";
        var requestId = Guid.NewGuid().ToString("N");
        try
        {
            var image = await RequestEraseCore(original, id, secret, requestId);
            SetResult(image);
            Status.Text = "处理完成。请对照原图检查题目和符号，再保存结果。";
        }
        catch (Exception ex) { Status.Text = $"处理未完成。请求编号：{requestId}"; MessageBox.Show($"{ex.Message}\n\n请求编号：{requestId}\n请先检查账户用量，再决定是否重试，以免重复消耗。", "处理失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        OpenButton.IsEnabled = !busy; PasteButton.IsEnabled = !busy; LoadResultButton.IsEnabled = !busy;
        BatchButton.IsEnabled = !busy; BatchQueuedButton.IsEnabled = !busy; PhoneButton.IsEnabled = !busy; PageList.IsEnabled = !busy; SelectionCanvas.IsEnabled = !busy; RemovePageButton.IsEnabled = !busy && currentPage is not null; EraseButton.IsEnabled = !busy && original is not null; RefreshRegions();
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = !busy && currentPage is not null;
    }
    private static async Task<BitmapImage> RequestEraseCore(byte[] bytes, string id, string secret, string requestId)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var raw = $"{id}_vision_SHA3-256_{nonce}_{timestamp}_{secret}";
        var signature = Convert.ToHexStringLower(SHA3_256.HashData(Encoding.UTF8.GetBytes(raw)));
        var body = new { dataBase64 = Convert.ToBase64String(bytes), dataType = "image", serviceOption = "scan", inputConfigs = "{\"function_option\":\"handwriting_remover\"}", outputConfigs = "{\"need_return_image\":\"True\"}", reqId = requestId, clientId = id, signMethod = "SHA3-256", signNonce = nonce, timestamp, signature };
        using var response = await Http.PostAsJsonAsync("https://scan-business.quark.cn/vision", body);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new Exception($"服务返回 HTTP {(int)response.StatusCode}。");
        using var json = JsonDocument.Parse(content);
        var encoded = FindImage(json.RootElement, 0);
        if (encoded is null)
        {
            var code = FindScalar(json.RootElement, "code"); var message = FindScalar(json.RootElement, "message") ?? FindScalar(json.RootElement, "msg");
            throw new Exception($"未收到处理后的图片。服务代码：{code ?? "未知"}；信息：{message ?? "无"}。");
        }
        if (encoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) encoded = encoded[(encoded.IndexOf(',') + 1)..];
        return LoadImage(Convert.FromBase64String(encoded));
    }
    private async void Batch_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        var added = ImportFiles(dialog.FileNames); if (added.Count > 0) await ProcessBatchAsync(added);
    }
    private async void BatchQueued_Click(object sender, RoutedEventArgs e)
    {
        var pending = pages.Where(p => p.OriginalBytes is not null && p.Processed is null).ToList();
        if (pending.Count == 0) { MessageBox.Show("列表中没有待处理的原始图片。", "批量处理"); return; }
        await ProcessBatchAsync(pending);
    }
    private async Task ProcessBatchAsync(List<Worksheet> pending)
    {
        var id = ClientId.Text.Trim(); var secret = ClientSecret.Password;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) { SetPage("settings"); MessageBox.Show("请先在设置中填写 Client ID 和 Client Secret。", "缺少凭据"); return; }
        var folder = new OpenFolderDialog { Title = "选择批量处理结果保存文件夹" };
        if (folder.ShowDialog() != true) return;
        if (MessageBox.Show($"即将逐张处理 {pending.Count} 张图片，最多发起 {pending.Count} 次 API 请求，可能消耗相应额度。遇到错误会停止，不自动重试。\n\n结果保存到：{folder.FolderName}\n\n继续吗？", "确认批量处理", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (RememberCredentials.IsChecked == true && !SaveCredentials()) return;
        stopBatch = false; SetBusy(true); StopBatchButton.IsEnabled = true;
        int completed = 0; string? failure = null; string? failedRequest = null;
        try
        {
            foreach (var page in pending)
            {
                if (stopBatch) break;
                var requestId = Guid.NewGuid().ToString("N");
                Status.Text = $"批量处理 {completed + 1}/{pending.Count}：{page.Name}";
                var started = DateTime.UtcNow;
                try
                {
                    var image = await RequestEraseCore(page.OriginalBytes!, id, secret, requestId);
                    page.Processed = image; page.Regions.Clear();
                    if (page == currentPage) ShowCurrentPage();
                    var name = page.Name + "_去手写"; var index = 0;
                    while (true)
                    {
                        var outputPath = Path.Combine(folder.FolderName, name + (index == 0 ? "" : $"_{index}") + ".png");
                        FileStream stream;
                        try { stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write); }
                        catch (IOException) when (File.Exists(outputPath)) { index++; continue; }
                        using (stream) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); }
                        break;
                    }
                    completed++;
                }
                catch (Exception ex) { failure = $"{page.Name}：{ex.Message}"; failedRequest = requestId; break; }
                var delay = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - started);
                if (delay > TimeSpan.Zero && !stopBatch) await Task.Delay(delay);
            }
        }
        finally { StopBatchButton.IsEnabled = false; SetBusy(false); ShowCurrentPage(); }
        Status.Text = $"批量结束：已保存 {completed}/{pending.Count} 张；处理结果已加入试卷列表。";
        var detail = failure is not null ? $"\n\n处理已停止：{failure}\n请求编号：{failedRequest}\n请先检查后台用量，再决定是否重试。" : stopBatch ? "\n\n已按请求停止；当前处理中的图片会先完成。" : "";
        MessageBox.Show($"已保存 {completed}/{pending.Count} 张到：\n{folder.FolderName}。\n可在试卷列表切换已处理的页面并框选错题。{detail}", "批量处理结果");
    }
    private void StopBatch_Click(object sender, RoutedEventArgs e)
    {
        stopBatch = true; StopBatchButton.IsEnabled = false; Status.Text = "正在停止：当前请求完成后不再提交下一张。";
    }
    private void Phone_Click(object sender, RoutedEventArgs e)
    {
        var window = new PhoneTransferWindow { Owner = this }; window.ShowDialog();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (result is null) return;
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = originalName + "_去手写.png", DefaultExt = ".png" };
        if (dialog.ShowDialog() != true) return;
        try { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result)); using var stream = File.Create(dialog.FileName); encoder.Save(stream); Status.Text = "已保存：" + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SetResult(BitmapSource image)
    {
        if (currentPage is null) return;
        currentPage.Processed = image; currentPage.Regions.Clear(); currentPage.RegionsChanged(); ShowCurrentPage();
    }
    private void ResetSelection()
    {
        selecting = false; selection = null; SelectionRect.Visibility = Visibility.Collapsed;
        if (SelectionCanvas.IsMouseCaptured) SelectionCanvas.ReleaseMouseCapture();
    }
    private void LoadResult_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;
        int added = 0; var errors = new List<string>();
        foreach (var path in dialog.FileNames)
        {
            try { var image = ImageOrientation.DecodeAndNormalize(File.ReadAllBytes(path)).Image; var page = new Worksheet(Path.GetFileNameWithoutExtension(path), null, null, image); pages.Add(page); PageList.SelectedItem = page; added++; }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(path)}：{ex.Message}"); }
        }
        SetPage("process"); Status.Text = $"已导入 {added} 张处理结果，可以切换页面并连续框选题目。";
        if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors.Take(8)), "部分图片无法导入");
    }
    private Point ClampPoint(Point point) => new(Math.Clamp(point.X, 0, ResultSurface.Width), Math.Clamp(point.Y, 0, ResultSurface.Height));
    private void Selection_Down(object sender, MouseButtonEventArgs e)
    {
        if (result is null || currentPage is null) return;
        ResetSelection(); selectionStart = ClampPoint(e.GetPosition(SelectionCanvas)); selecting = true;
        SelectionCanvas.CaptureMouse(); SelectionRect.Visibility = Visibility.Visible;
        UpdateSelection(selectionStart); e.Handled = true;
    }
    private void Selection_Move(object sender, MouseEventArgs e) { if (selecting) UpdateSelection(ClampPoint(e.GetPosition(SelectionCanvas))); }
    private void Selection_Up(object sender, MouseButtonEventArgs e)
    {
        if (!selecting || result is null || currentPage is null) return;
        var end = ClampPoint(e.GetPosition(SelectionCanvas));
        var left = (int)Math.Floor(Math.Min(selectionStart.X, end.X)); var top = (int)Math.Floor(Math.Min(selectionStart.Y, end.Y));
        var right = Math.Min(result.PixelWidth, (int)Math.Ceiling(Math.Max(selectionStart.X, end.X))); var bottom = Math.Min(result.PixelHeight, (int)Math.Ceiling(Math.Max(selectionStart.Y, end.Y)));
        selecting = false; SelectionCanvas.ReleaseMouseCapture(); SelectionRect.Visibility = Visibility.Collapsed;
        if (right - left < 2 || bottom - top < 2) return;
        currentPage.Regions.Add(new Int32Rect(left, top, right - left, bottom - top)); currentPage.RegionsChanged(); RefreshRegions();
        Status.Text = $"当前页已框选 {currentPage.Regions.Count} 个区域；可继续框选或加入错题集。";
    }
    private void Selection_LostCapture(object sender, MouseEventArgs e) { if (selecting) ResetSelection(); }
    private void UpdateSelection(Point end)
    {
        Canvas.SetLeft(SelectionRect, Math.Min(selectionStart.X, end.X)); Canvas.SetTop(SelectionRect, Math.Min(selectionStart.Y, end.Y));
        SelectionRect.Width = Math.Abs(end.X - selectionStart.X); SelectionRect.Height = Math.Abs(end.Y - selectionStart.Y);
    }
    private void RefreshRegions()
    {
        if (SelectionCanvas is null) return;
        for (int i = SelectionCanvas.Children.Count - 1; i >= 0; i--) if (SelectionCanvas.Children[i] != SelectionRect) SelectionCanvas.Children.RemoveAt(i);
        var regions = currentPage?.Regions; selection = regions?.Count > 0 ? regions[^1] : null;
        if (regions is not null) foreach (var rect in regions)
        {
            var box = new System.Windows.Shapes.Rectangle { Width = rect.Width, Height = rect.Height, Stroke = System.Windows.Media.Brushes.DodgerBlue, StrokeThickness = Math.Max(3, ResultSurface.Width / 400), Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(45, 40, 116, 239)), IsHitTestVisible = false };
            Canvas.SetLeft(box, rect.X); Canvas.SetTop(box, rect.Y); SelectionCanvas.Children.Insert(0, box);
        }
        var any = !isBusy && selection is not null; SaveCropButton.IsEnabled = any; AddCropButton.IsEnabled = any; UndoRegionButton.IsEnabled = any; ClearRegionButton.IsEnabled = any;
        AddAllRegionsButton.IsEnabled = !isBusy && pages.Any(p => p.Processed is not null && p.Regions.Count > 0);
    }
    private void UndoRegion_Click(object sender, RoutedEventArgs e)
    {
        if (currentPage is null || currentPage.Regions.Count == 0) return; currentPage.Regions.RemoveAt(currentPage.Regions.Count - 1); currentPage.RegionsChanged(); RefreshRegions();
    }
    private void ClearRegion_Click(object sender, RoutedEventArgs e)
    {
        if (currentPage is null) return; currentPage.Regions.Clear(); currentPage.RegionsChanged(); RefreshRegions();
    }

    private void OutputScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateOutputZoom();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { outputZoom = Math.Min(4, outputZoom + 0.5); UpdateOutputZoom(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { outputZoom = Math.Max(1, outputZoom - 0.5); UpdateOutputZoom(); }
    private void UpdateOutputZoom()
    {
        if (OutputScroll is null || OutputView is null || ZoomText is null) return;
        OutputView.Width = Math.Max(1, OutputScroll.ActualWidth * outputZoom);
        OutputView.Height = Math.Max(1, OutputScroll.ActualHeight * outputZoom);
        ZoomText.Text = $"{outputZoom * 100:0}%";
    }

    private void SaveCrop_Click(object sender, RoutedEventArgs e)
    {
        if (result is null || selection is null) return;
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = originalName + "_错题.png", DefaultExt = ".png" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var crop = new CroppedBitmap(result, selection.Value);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(crop));
            using var stream = File.Create(dialog.FileName); encoder.Save(stream);
            Status.Text = "错题已保存：" + dialog.FileName;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void AddCrop_Click(object sender, RoutedEventArgs e)
    {
        if (currentPage is not null) AddSelectedRegions(new[] { currentPage });
    }
    private void AddAllRegions_Click(object sender, RoutedEventArgs e) => AddSelectedRegions(pages.ToArray());
    private void AddSelectedRegions(IEnumerable<Worksheet> sourcePages)
    {
        var target = TargetCollection.SelectedItem as string ?? library.Collections.FirstOrDefault() ?? "默认错题集";
        var subject = TargetSubject.SelectedItem as string; if (subject == "未分类") subject = "";
        var selectedPages = sourcePages.Where(p => p.Processed is not null && p.Regions.Count > 0).ToArray();
        var added = new List<QuestionItem>();
        foreach (var page in selectedPages)
        {
            foreach (var region in page.Regions)
            {
                var crop = new CroppedBitmap(page.Processed!, region); crop.Freeze();
                added.Add(new QuestionItem("未命名错题", crop, Subject: subject ?? "", Collection: target));
            }
        }
        if (added.Count == 0) return;
        if (!CommitQuestions(questions.Concat(added).ToArray())) return;
        foreach (var page in selectedPages) { page.Regions.Clear(); page.RegionsChanged(); }
        RefreshRegions();
        Status.Text = $"已将 {added.Count} 道题加入“{target}”，可在错题集页面编辑名称与分类。";
    }
    private bool QuestionMatches(object value)
    {
        if (value is not QuestionItem item) return false;
        if (selectedCollection != "全部错题" && item.Collection != selectedCollection) return false;
        if (selectedSubject != "全部学科" && item.Subject != selectedSubject) return false;
        var query = SearchText?.Text.Trim() ?? "";
        if (query.Length > 0 && !new[] { item.Label, item.Subject, item.Topic, item.Notes }.Any(text => text.Contains(query, StringComparison.CurrentCultureIgnoreCase))) return false;
        var filter = (ReviewFilter?.SelectedItem as ComboBoxItem)?.Content as string ?? "全部";
        return filter switch
        {
            "待复习" => item.ReviewStatus == "待复习",
            "已掌握" => item.ReviewStatus == "已掌握",
            "复习中" => item.ReviewStatus == "复习中",
            "到期复习" => item.ReviewStatus != "已掌握" && item.NextReview is DateTime date && date.Date <= DateTime.Today,
            _ => true
        };
    }
    private void FilterChanged(object sender, RoutedEventArgs e) => questionView?.Refresh();
    private QuestionItem[] FilteredQuestions() => questionView?.Cast<QuestionItem>().ToArray() ?? Array.Empty<QuestionItem>();
    private QuestionItem? QuestionAt(DependencyObject? originalSource)
    {
        if (originalSource is null) return null;
        return (ItemsControl.ContainerFromElement(QuestionList, originalSource) as ListBoxItem)?.DataContext as QuestionItem;
    }
    private void QuestionList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(QuestionList); dragQuestion = QuestionAt(e.OriginalSource as DependencyObject);
    }
    private void QuestionList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragQuestion is null) return;
        var position = e.GetPosition(QuestionList);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = dragQuestion; dragQuestion = null;
        try { DragDrop.DoDragDrop(QuestionList, item, DragDropEffects.Move); }
        finally { ClearDropMarker(); }
    }
    private void QuestionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = QuestionAt(e.OriginalSource as DependencyObject); if (item is null) return;
        dragQuestion = null; QuestionList.SelectedItem = item; EditQuestion_Click(sender, e); e.Handled = true;
    }
    private ListBoxItem? DropContainer(DependencyObject? source) => source is null ? null : ItemsControl.ContainerFromElement(QuestionList, source) as ListBoxItem;
    private void ClearDropMarker()
    {
        if (dropMarker is not null && dropMarkerLayer is not null) dropMarkerLayer.Remove(dropMarker);
        dropMarker = null; dropMarkerLayer = null;
    }
    private void QuestionList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(QuestionItem)) is not QuestionItem source) return;
        var container = DropContainer(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not QuestionItem target || ReferenceEquals(source, target)) { ClearDropMarker(); e.Effects = DragDropEffects.None; e.Handled = true; return; }
        bool before = e.GetPosition(container).X < container.ActualWidth / 2;
        if (dropMarker?.AdornedElement != container || dropMarker.Before != before)
        {
            ClearDropMarker(); var layer = AdornerLayer.GetAdornerLayer(container);
            if (layer is not null) { dropMarker = new QuestionDropAdorner(container, before); dropMarkerLayer = layer; layer.Add(dropMarker); }
        }
        e.Effects = DragDropEffects.Move; e.Handled = true;
    }
    private void QuestionList_DragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(QuestionList);
        if (point.X < 0 || point.Y < 0 || point.X > QuestionList.ActualWidth || point.Y > QuestionList.ActualHeight) ClearDropMarker();
    }
    private void QuestionList_Drop(object sender, DragEventArgs e)
    {
        ClearDropMarker();
        if (e.Data.GetData(typeof(QuestionItem)) is not QuestionItem source) return;
        var container = DropContainer(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not QuestionItem target || ReferenceEquals(source, target)) { e.Handled = true; return; }
        var from = questions.IndexOf(source); var targetIndex = questions.IndexOf(target);
        bool before = e.GetPosition(container).X < container.ActualWidth / 2;
        var to = QuestionDropAdorner.DestinationIndex(from, targetIndex, before);
        if (from >= 0 && targetIndex >= 0 && to >= 0 && to < questions.Count && from != to)
        {
            questions.Move(from, to); QuestionList.SelectedItem = source;
            Status.Text = basketStorageAvailable ? "顺序已更新并自动保存。" : "顺序仅在当前窗口中更新，自动保存不可用。";
        }
        e.Handled = true;
    }
    private void EditQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionList.SelectedItem is not QuestionItem selected) { MessageBox.Show("请先选中一道错题。", "编辑错题"); return; }
        var editor = new QuestionEditorWindow(selected) { Owner = this };
        if (editor.ShowDialog() != true || editor.Edited is null) return;
        var index = questions.IndexOf(selected); if (index < 0) return;
        questions[index] = editor.Edited;
        SyncLibrary(); RefreshLibraryControls();
        QuestionList.SelectedItem = editor.Edited;
        Status.Text = "错题信息已更新。";
    }
    private void MoveQuestionCollection_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionList.SelectedItem is not QuestionItem selected) { MessageBox.Show("请先选择一道错题。"); return; }
        var dialog = new Window { Title = "移动到错题集", Owner = this, Width = 440, Height = 230, ResizeMode = ResizeMode.NoResize };
        UiFactory.Configure(dialog);
        var root = new DockPanel { Margin = new Thickness(22) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "移动", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) }; UiFactory.Primary(ok);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(16, 6, 16, 6) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "移动到错题集", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) }); panel.Children.Add(new TextBlock { Text = "选择目标错题集", Margin = new Thickness(0, 0, 0, 8) });
        var choice = new ComboBox { ItemsSource = library.Collections.ToArray(), SelectedItem = selected.Collection, Padding = new Thickness(6) }; panel.Children.Add(choice); root.Children.Add(panel); dialog.Content = root;
        ok.Click += (_, _) => { if (choice.SelectedItem is string) dialog.DialogResult = true; };
        if (dialog.ShowDialog() != true || choice.SelectedItem is not string target) return;
        var index = questions.IndexOf(selected); if (index >= 0) { var changed = selected with { Collection = target }; questions[index] = changed; QuestionList.SelectedItem = changed; }
    }
    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionList.SelectedItem is not QuestionItem selected) return;
        var visible = FilteredQuestions(); var visibleIndex = Array.IndexOf(visible, selected);
        if (visibleIndex <= 0) return;
        var from = questions.IndexOf(selected); var to = questions.IndexOf(visible[visibleIndex - 1]);
        questions.Move(from, to); QuestionList.SelectedItem = selected;
    }
    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionList.SelectedItem is not QuestionItem selected) return;
        var visible = FilteredQuestions(); var visibleIndex = Array.IndexOf(visible, selected);
        if (visibleIndex < 0 || visibleIndex >= visible.Length - 1) return;
        var from = questions.IndexOf(selected); var to = questions.IndexOf(visible[visibleIndex + 1]);
        questions.Move(from, to); QuestionList.SelectedItem = selected;
    }
    private void RemoveQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionList.SelectedItem is QuestionItem selected) questions.Remove(selected);
    }
    private void ClearQuestions_Click(object sender, RoutedEventArgs e)
    {
        if (questions.Count == 0) return;
        if (MessageBox.Show("确定清空全部错题吗？包括当前筛选条件下未显示的题目。", "清空错题篮", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) questions.Clear();
    }
    private void ExportPage_Click(object sender, RoutedEventArgs e)
    {
        if (result is not null) ExportPdf(new[] { result }, originalName + "_去手写.pdf");
    }
    private void PreviewQuestions_Click(object sender, RoutedEventArgs e)
    {
        var visible = FilteredQuestions();
        if (visible.Length == 0) { MessageBox.Show("当前筛选条件下没有错题。", "没有可预览的题目"); return; }
        var images = visible.Select(q => q.Image).ToArray();
        var preview = new PreviewWindow(images) { Owner = this };
        if (preview.ShowDialog() == true && preview.ExportedPath is not null) Status.Text = "PDF 已保存：" + preview.ExportedPath;
    }
    private void ExportQuestions_Click(object sender, RoutedEventArgs e)
    {
        PreviewQuestions_Click(sender, e);
    }
    private void ExportPdf(IReadOnlyList<BitmapSource> images, string fileName)
    {
        var dialog = new SaveFileDialog { Filter = "PDF 文档|*.pdf", FileName = fileName, DefaultExt = ".pdf" };
        if (dialog.ShowDialog() != true) return;
        try { PdfExporter.Save(dialog.FileName, images); Status.Text = "PDF 已保存：" + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static BitmapImage LoadImage(byte[] data)
    {
        using var stream = new MemoryStream(data); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
    private static string? FindImage(JsonElement element, int depth)
    {
        if (depth > 12) return null;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var item in element.EnumerateObject()) { if (item.Name.Equals("ImageBase64", StringComparison.OrdinalIgnoreCase) && item.Value.ValueKind == JsonValueKind.String) return item.Value.GetString(); var found = FindImage(item.Value, depth + 1); if (found is not null) return found; }
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) { var found = FindImage(item, depth + 1); if (found is not null) return found; }
        if (element.ValueKind == JsonValueKind.String) { var value = element.GetString(); if (value is not null && value.TrimStart().StartsWith('{')) { try { using var nested = JsonDocument.Parse(value); return FindImage(nested.RootElement, depth + 1); } catch (JsonException) { } } }
        return null;
    }
    private static string? FindScalar(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number) return value.ToString();
        return null;
    }
}
