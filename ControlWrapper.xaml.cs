using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xbim.Presentation;
using Xbim.Presentation.LayerStyling;

namespace DrawControl3dWrapper
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void CallBackDelegate([MarshalAs(UnmanagedType.BStr)]string guid);

    /// <summary>
    /// Логика взаимодействия для UserControl1.xaml
    /// </summary>
    [Guid("b56a2d4c-a21e-4504-ab79-2c3e5db01523") , //ClassInterface(ClassInterfaceType.None),
        ComVisible(true)]
    public partial class ControlWrapper : UserControl, INotifyPropertyChanged, IControlWrapper
    {
        public ControlWrapper()
        {
            InitializeComponent();
            Canvas.ApplyTemplate();
            DataContext = this;
        }

        private BackgroundWorker _loadFileBackgroundWorker;

        private string _openedModelFileName;
        public string Title { get; set; }
        public string GetOpenedModelFileName()
        {
            return _openedModelFileName;
        }
        
        /// <summary>
        /// Allow context menu of drawingcontrol
        /// </summary>
        private Visibility _AllowCanvasMenu = Visibility.Hidden;
        public Visibility AllowCanvasMenu { get { return _AllowCanvasMenu; } }
        long _Timestamp;

        private void DrawingControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _Timestamp = DateTime.Now.Ticks;
        }
        private void DrawingControl_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var tiks = DateTime.Now.Ticks - _Timestamp;
            if (tiks < 2000000 && SelectedItem != null)
            {
                _AllowCanvasMenu = Visibility.Visible;
            }
            else
            {
                _AllowCanvasMenu = Visibility.Collapsed;
            }
            OnPropertyChanged("AllowCanvasMenu");
//            OnPropertyChanged("CallFromBW");

            e.Handled = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetOpenedModelFileName(string ifcFilename)
        {
            _openedModelFileName = ifcFilename;
            // try to update the window title through a delegate for multithreading
            Dispatcher.BeginInvoke(new Action(delegate
            {
                Title = string.IsNullOrEmpty(ifcFilename)
                    ? "Xbim Xplorer" :
                    "Xbim Xplorer - [" + ifcFilename + "]";
            }));
        }

        private void CloseAndDeleteTemporaryFiles()
        {
            try
            {
                if (_loadFileBackgroundWorker != null && _loadFileBackgroundWorker.IsBusy)
                    _loadFileBackgroundWorker.CancelAsync(); //tell it to stop

                SetOpenedModelFileName(null);
                if (Model != null)
                {
                    Model.Dispose();
                    ModelProvider.ObjectInstance = null;
                    ModelProvider.Refresh();
                }
                if (!(Canvas.DefaultLayerStyler is SurfaceLayerStyler))
                    SetDefaultModeStyler(null, null);
            }
            finally
            {
                if (!(_loadFileBackgroundWorker != null && _loadFileBackgroundWorker.IsBusy && _loadFileBackgroundWorker.CancellationPending)) //it is still busy but has been cancelled 
                {
                    if (!string.IsNullOrWhiteSpace(_temporaryXbimFileName) && File.Exists(_temporaryXbimFileName))
                        File.Delete(_temporaryXbimFileName);
                    _temporaryXbimFileName = null;
                } //else do nothing it will be cleared up in the worker thread
            }
        }

        private void SetDefaultModeStyler(object sender, RoutedEventArgs e)
        {
            Canvas.DefaultLayerStyler = new SurfaceLayerStyler();
            ConnectStylerFeedBack();
            Canvas.ReloadModel();
        }
        private void ConnectStylerFeedBack()
        {
            if (Canvas.DefaultLayerStyler is IProgressiveLayerStyler)
            {
                ((IProgressiveLayerStyler)Canvas.DefaultLayerStyler).ProgressChanged += OnProgressChanged;
            }
        }

        private void OnProgressChanged(object sender, ProgressChangedEventArgs e)
        {
                //if (args.ProgressPercentage < 0 || args.ProgressPercentage > 100)
                //    return;

                //Application.Current.Dispatcher.BeginInvoke(
                //    DispatcherPriority.Send,
                //    new Action(() =>
                //    {
                //        ProgressBar.Value = args.ProgressPercentage;
                //        StatusMsg.Text = (string)args.UserState;
                //    }));
        }
        void log(string txt)
        {
            File.AppendAllText("C:\\users\\swadm\\1.log", txt);
        }

        CallBackDelegate _Callback;
        public void SetCallback(IntPtr callback)
        {
            _Callback = Marshal.GetDelegateForFunctionPointer<CallBackDelegate>(callback);
            OnPropertyChanged("CallFromBW");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modelFileName"></param>
        public void LoadAnyModel(string modelFileName)
        {
            var fInfo = new FileInfo(modelFileName);
            if (!fInfo.Exists) // file does not exist; do nothing
            {
                log("No " + modelFileName + " file\n");

                return;
            }
            if (fInfo.FullName.ToLower() == GetOpenedModelFileName()) //same file do nothing
            {
                log("file name have been loaded\n");
                return;
            }
            log("start " +modelFileName+ "\n");
            // there's no going back; if it fails after this point the current file should be closed anyway
            CloseAndDeleteTemporaryFiles();
            SetOpenedModelFileName(modelFileName.ToLower());
            //ProgressStatusBar.Visibility = Visibility.Visible;
            SetWorkerForFileLoad();

            var ext = fInfo.Extension.ToLower();
            switch (ext)
            {
                case ".ifc": //it is an Ifc File
                case ".ifcxml": //it is an IfcXml File
                case ".ifczip": //it is a zip file containing xbim or ifc File
                case ".zip": //it is a zip file containing xbim or ifc File
                case ".xbimf":
                case ".xbim":
                    _loadFileBackgroundWorker.RunWorkerAsync(modelFileName);
                    break;
                default:
        //            Logger?.LogWarning("Extension '{extension}' has not been recognised.", ext);
                    break;
            }
            log("loading has been started\n");
        }
        private void SetWorkerForFileLoad()
        {
            _loadFileBackgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _loadFileBackgroundWorker.ProgressChanged += OnProgressChanged;
            _loadFileBackgroundWorker.DoWork += OpenAcceptableExtension;
            _loadFileBackgroundWorker.RunWorkerCompleted += FileLoadCompleted;
        }
        private void SetDeflection(Xbim.Common.IModel model)
        {
            var mf = model.ModelFactors;
            if (mf == null)
                return;
            if (!double.IsNaN(_angularDeflectionOverride))
                mf.DeflectionAngle = _angularDeflectionOverride;
            if (!double.IsNaN(_deflectionOverride))
                mf.DeflectionTolerance = mf.OneMilliMetre * _deflectionOverride;
        }

        private double _deflectionOverride = double.NaN;
        private double _angularDeflectionOverride = double.NaN;

        private bool _meshModel = true;
        public XbimDBAccess FileAccessMode { get; set; } = XbimDBAccess.Read;
        private void OpenAcceptableExtension(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var selectedFilename = args.Argument as string;

            try
            {
                if (worker == null)
                    throw new Exception("Background thread could not be accessed");
                _temporaryXbimFileName = System.IO.Path.GetTempFileName();
                SetOpenedModelFileName(selectedFilename);
                var model = IfcStore.Open(selectedFilename, null, null, worker.ReportProgress, FileAccessMode);
                if (_meshModel)
                {
                    // mesh direct model
                    if (model.GeometryStore.IsEmpty)
                    {
                        try
                        {
                            var context = new Xbim3DModelContext(model);

#if FastExtrusion
                            context.UseSimplifiedFastExtruder = _simpleFastExtrusion;
#endif
                            SetDeflection(model);
                            //upgrade to new geometry representation, uses the default 3D model
                            context.CreateContext(worker.ReportProgress, true);
                        }
                        catch (Exception geomEx)
                        {
                            var sb = new StringBuilder();
                            sb.AppendLine($"Error creating geometry context of '{selectedFilename}' {geomEx.StackTrace}.");
                            var newexception = new Exception(sb.ToString(), geomEx);
                            log($"Error creating geometry context of {selectedFilename} " + geomEx.ToString() + "\n");
                            //Logger?.LogError(0, newexception, "Error creating geometry context of {filename}", selectedFilename);
                        }
                    }

                    // mesh references
                    foreach (var modelReference in model.ReferencedModels)
                    {
                        // creates federation geometry contexts if needed
                        //Debug.WriteLine(modelReference.Name);
                        if (modelReference.Model == null)
                            continue;
                        if (!modelReference.Model.GeometryStore.IsEmpty)
                            continue;
                        var context = new Xbim3DModelContext(modelReference.Model);
                        //if (!_multiThreading)
                            //context.MaxThreads = 1;
#if FastExtrusion
                        context.UseSimplifiedFastExtruder = _simpleFastExtrusion;
#endif
                        SetDeflection(modelReference.Model);
                        //upgrade to new geometry representation, uses the default 3D model
                        context.CreateContext(worker.ReportProgress, true);
                    }
                    if (worker.CancellationPending)
                    //if a cancellation has been requested then don't open the resulting file
                    {
                        try
                        {
                            model.Close();
                            if (File.Exists(_temporaryXbimFileName))
                                File.Delete(_temporaryXbimFileName); //tidy up;
                            _temporaryXbimFileName = null;
                            SetOpenedModelFileName(null);
                        }
                        catch (Exception ex)
                        {
                            //Logger?.LogError(0, ex, "Failed to cancel open of model {filename}", selectedFilename);
                            log($"Failed to cancel open of model {selectedFilename}");
                        }
                        return;
                    }
                }
                else
                {
                    //Logger?.LogWarning("Settings prevent mesh creation.");
                    log("WARN: Settings prevent mesh creation.");
                }
                args.Result = model;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Error opening '{selectedFilename}' {ex.StackTrace}.");
                var newexception = new Exception(sb.ToString(), ex);
                //Logger?.LogError(0, ex, "Error opening {filename}", selectedFilename);
                log($"Error opening '{selectedFilename}'\n");
                args.Result = newexception;
            }
        }

        private void FileLoadCompleted(object s, RunWorkerCompletedEventArgs args)
        {
            if (args.Result is IfcStore) //all ok
            {
                //this Triggers the event to load the model into the views 
                this.Dispatcher.Invoke(() =>
                {
                    ModelProvider.ObjectInstance = args.Result;
                    ModelProvider.Refresh();
                    //ProgressBar.Value = 0;
                    //StatusMsg.Text = "Ready";
                    //AddRecentFile();
                });
            }
            else //we have a problem
            {
                this.Dispatcher.Invoke(() =>
                {
                    var errMsg = args.Result as string;
                    if (!string.IsNullOrEmpty(errMsg))
                        MessageBox.Show(errMsg, "Error Opening File", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None, MessageBoxOptions.None);
                    var exception = args.Result as Exception;
                    if (exception != null)
                    {
                        var sb = new StringBuilder();

                        var indent = "";
                        while (exception != null)
                        {
                            sb.AppendFormat("{0}{1}\n", indent, exception.Message);
                            exception = exception.InnerException;
                            indent += "\t";
                        }
                        MessageBox.Show(sb.ToString(), "Error Opening Ifc File", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None, MessageBoxOptions.None);
                    }
                    SetOpenedModelFileName("");
                });
            }
            FireLoadingComplete(s, args);
        }
        private void FireLoadingComplete(object s, RunWorkerCompletedEventArgs args)
        {
            if (LoadingComplete != null)
            {
                LoadingComplete(s, args);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="s"></param>
        /// <param name="args"></param>
        public delegate void LoadingCompleteEventHandler(object s, RunWorkerCompletedEventArgs args);
        /// <summary>
        /// 
        /// </summary>
        public event LoadingCompleteEventHandler LoadingComplete;

        /// <summary>
        /// 
        /// </summary>
        public IPersistEntity SelectedItem
        {
            get { return (IPersistEntity)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SelectedItem.  This enables animation, styling, binding, etc...
        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register("SelectedItem", typeof(IPersistEntity), typeof(ControlWrapper),
                                        new UIPropertyMetadata(null, OnSelectedItemChanged));


        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            //var mw = d as ControlWrapper;
            //if (mw != null && e.NewValue is IPersistEntity)
            //{
            //    var label = (IPersistEntity)e.NewValue;
            //    mw.EntityLabel.Text = label != null ? "#" + label.EntityLabel : "";
            //}
            //else if (mw != null) mw.EntityLabel.Text = "";
        }


        private ObjectDataProvider ModelProvider
        {
            get
            {
                return MainFrame.DataContext as ObjectDataProvider;
            }
        }

        public IfcStore Model
        {
            get
            {
                var op = MainFrame.DataContext as ObjectDataProvider;
                return op == null ? null : op.ObjectInstance as IfcStore;
            }
        }

        private string _temporaryXbimFileName;

        private void SpatialControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {

        }
        private bool getPropertyValue(IPersistEntity entity, HashSet<ExpressType> type, int comparable) // labvit: sheetcode
        {
            ExpressMetaProperty obType = null;
            if (type == null) return false;
            if (type != null && !type.Contains(entity.ExpressType)) return false;
            obType = entity.ExpressType.Properties.ToList()[4].Value;
            var ob = obType.PropertyInfo.GetValue(entity);
            if (ob != null)
            {
                return comparable == ob.GetHashCode();// GetType().GetProperty("Value").GetValue(ob).GetHashCode();
            }

            return false;
        }

        private int getPropertyValue(IPersistEntity entity, HashSet<ExpressType> type) // labvit: sheetcode
        {
            //int strLength = "ObjectType".Length;
            int retVal = 0;
            ExpressMetaProperty obType = null;
            if (type == null) return 0;
            if (type != null && !type.Contains(entity.ExpressType)) return 0;
            obType = entity.ExpressType.Properties.ToList()[4].Value; // hard coding, it may be realised function below
                                                                      //foreach (var prop in entity.ExpressType.Properties)
                                                                      //    if (string.Compare(prop.Value.Name, "ObjectType", false) == 0)
                                                                      //    {
                                                                      //        obType = prop.Value;
                                                                      //        obType = prop.Value ;
            var ob = obType.PropertyInfo.GetValue(entity);
            if (ob != null)
            {
                retVal = ob.GetHashCode();// GetType().GetProperty("Value").GetValue(ob).GetHashCode();
            }
            //        break;
            //  }

            return retVal;
        }

        Xbim.Presentation.EntitySelection getTheSameElements(IPersistEntity selectedItem, HashSet<ExpressType> types)
        {
            Xbim.Presentation.EntitySelection sel = new Xbim.Presentation.EntitySelection(false);
            var obType = getPropertyValue(selectedItem, types);
            if (0 != obType)
            {
                // get all objects with the same objectType
                var project = Model.Instances.FirstOrDefault<IIfcProject>();
                if (project is Xbim.Ifc2x3.Kernel.IfcProject)
                {
                    var selected = new List<Xbim.Ifc2x3.Interfaces.IIfcRoot>();
                    foreach (var item in Model.Instances.OfType<Xbim.Ifc2x3.Interfaces.IIfcRoot>())
                    {
                        if (getPropertyValue(item, types, obType))
                            selected.Add(item);

                    }
                    if (selected.Any())
                        sel.AddRange(selected);
                }
                else if (project is Xbim.Ifc4.Kernel.IfcProject)
                {
                    var selected = Model.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcRoot>()
                        .Where(x =>
                        getPropertyValue(x, types, obType)
                    ).ToList();

                    if (selected != null)
                        sel.AddRange(selected);

                }
                else
                    throw new Exception("Undefined project");


            }
            return sel;
        }

        private void selectTheSameElements(IPersistEntity selectedItem, HashSet<ExpressType> types)
        {
            if (Model != null && selectedItem != null)
            {
                var sel = getTheSameElements(selectedItem, types);
                if (sel.Any())
                    Canvas.SetSelection(sel);
            }
        }

        public int Select(string[] guids /*,out string[] notFindedGuid*/)
        {
            try
            {
                var _notFindedGuid = new List<string>();
                try
                {
                    if (Model != null)
                    {
                        //if (guids.Length ==1)
                        {
                            Xbim.Presentation.EntitySelection sel = new Xbim.Presentation.EntitySelection(false);
                            var project = Model.Instances.FirstOrDefault<IIfcProject>();
                            if (project is Xbim.Ifc2x3.Kernel.IfcProject)
                            {

                                var globalIds = guids.Select(g => new Xbim.Ifc2x3.UtilityResource.IfcGloballyUniqueId(g)).ToList();

                                var selected = Model.Instances.OfType<Xbim.Ifc2x3.Interfaces.IIfcRoot>().Join(globalIds, a => a.GlobalId, b => b, (a, b) => a).ToArray();
                                _notFindedGuid = guids.Except(selected.Select(x => x.GlobalId.Value.ToString())).ToList();
                                sel.AddRange(selected);


                            }
                            else if (project is Xbim.Ifc4.Kernel.IfcProject)
                            {
                                var globalIds = guids.Select(g => new Xbim.Ifc4.UtilityResource.IfcGloballyUniqueId(g)).ToList();
                                var selected = Model.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcRoot>().Join(globalIds, a => a.GlobalId, b => b, (a, b) => a).ToArray();
                                //                                selected[0].ExpressType.Type
                                _notFindedGuid = guids.Except(selected.Select(x => x.GlobalId.Value.ToString())).ToList();
                                sel.AddRange(selected);
                            }
                            else
                                throw new Exception("Undefined project");

                            if (!_notFindedGuid.Any())
                                Canvas.SetSelection(sel);
                            else
                                return 1;
                            //DrawingControl.ReloadModel(Xbim.Presentation.DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
                            //DrawingControl.ReloadModel();


                            return 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log(ex.ToString());

                } // if some errors occurs return 1;
                return 1;

                //notFindedGuid = _notFindedGuid.ToArray(); // TODO: comment for the first time

            }
            catch { return 1; }
        }




        public int SelectTheSame(string guid)
        {
            try
            {
                log("Model: " + (Model != null));
                if (Model != null)
                {

                    IPersistEntity selected = null;
                    Xbim.Presentation.EntitySelection sel = new Xbim.Presentation.EntitySelection(false);
                    var project = Model.Instances.FirstOrDefault<IIfcProject>();
                    if (project is Xbim.Ifc2x3.Kernel.IfcProject)
                    {

                        var globalIds = new Xbim.Ifc2x3.UtilityResource.IfcGloballyUniqueId(guid);

                        selected = Model.Instances.OfType<Xbim.Ifc2x3.Interfaces.IIfcRoot>().FirstOrDefault(a => a.GlobalId == globalIds);

                    }
                    else if (project is Xbim.Ifc4.Kernel.IfcProject)
                    {
                        var globalIds = new Xbim.Ifc4.UtilityResource.IfcGloballyUniqueId(guid);
                        selected = Model.Instances.OfType<Xbim.Ifc4.Interfaces.IIfcRoot>().FirstOrDefault(a => a.GlobalId == globalIds);

                    }
                    else
                        throw new Exception("Undefined project");
                    //System.Timers.Timer t = new System.Timers.Timer();
                    //t.Start();
                    if (selected != null)
                    {
                        var test = getExpressType(Model);

                        selectTheSameElements(selected, test);
                        log("Set selection");
                    }
                    else
                        return 1;
                    //t.Stop();
                    //Console.WriteLine( t.Interval.ToString());
                    log("return 0");
                    return 0;
                }

            }
            catch (Exception ex)
            {
                log("Не удалось выделить " + ex.ToString());
            }
            log("SelectTheSame return 0");
            return 0;
        }

        private void SelecetTheSame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Model != null)
                    selectTheSameElements(SelectedItem, getExpressType(Model));
            }
            catch (Exception ex)
            {
                log("Не удалось выделить " + ex.ToString());
            }
            
        }
        HashSet<ExpressType> getExpressType(IModel model)
        {
            HashSet<ExpressType> table = new HashSet<ExpressType>();
            foreach (var type in model.Metadata.Types())
            {
                foreach (var prop in type.Properties.Values)
                    if (string.Compare(prop.Name, "ObjectType", false) == 0)
                    {

                        table.Add(type);
                        break;
                    }

            }
            return table;
        }

        private void HideSelected(object sender, RoutedEventArgs e)
        {
            hideSelected(Canvas.Selection);
        }
        private void hideSelected(EntitySelection selection)
        {
            if (null != Canvas.HiddenInstances)
                Canvas.HiddenInstances.AddRange(selection);
            else
                Canvas.HiddenInstances = selection.ToList();

            Canvas.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
        }

        private void HideTheSameSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Model != null && SelectedItem != null)
                {
                    var types = getExpressType(Model);
                    var sel = getTheSameElements(SelectedItem, types);
                    if (sel.Any())
                        hideSelected(sel);
                }
            }
            catch (Exception ex)
            {
                log("Не удалось скрыть элементы: " + ex.ToString());
            }

        }

        private void IsolateSelected(object sender, RoutedEventArgs e)
        {
            isolateSelected(Canvas.Selection);

        }

        private void isolateSelected(EntitySelection selection)
        {
            Canvas.IsolateInstances = selection.ToList();
            Canvas.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
        }

        private void IsolateTheSameSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Model != null && SelectedItem != null)
                {
                    var types = getExpressType(Model);
                    var sel = getTheSameElements(SelectedItem, types);
                    if (sel.Any())
                        isolateSelected(sel);
                }
            }
            catch (Exception ex)
            {
                log("Не удалось изолировать: " + ex.ToString());
            }
        }

        private void RestoreView(object sender, RoutedEventArgs e)
        {
            Canvas.IsolateInstances = null;
            Canvas.HiddenInstances = null;
            Canvas.ReloadModel(DrawingControl3D.ModelRefreshOptions.ViewPreserveCameraPosition);
        }

        public bool CallFromBW => _Callback != null;

        private void ViewToBW(object sender, RoutedEventArgs e)
        {
            if(SelectedItem!=null)
            {
                var project = Model.Instances.FirstOrDefault<IIfcProject>();
                if (project is Xbim.Ifc2x3.Kernel.IfcProject)
                {
                    var sel = SelectedItem as Xbim.Ifc2x3.Interfaces.IIfcRoot;
                    if (sel != null)
                        _Callback?.Invoke( sel.GlobalId.ToString());
                }
                else if (project is Xbim.Ifc4.Kernel.IfcProject)
                {
                    var selected = SelectedItem as Xbim.Ifc4.Interfaces.IIfcRoot;
                    if (selected != null)
                        _Callback?.Invoke(selected.GlobalId.ToString());

                }
            }
        }
    }
}
 