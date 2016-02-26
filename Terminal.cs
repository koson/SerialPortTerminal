/* 
 * Project:    SerialPort Terminal
 * Company:    Coad .NET, http://coad.net
 * Author:     Noah Coad, http://coad.net/noah
 * Created:    March 2005
 * 
 * Notes:      This was created to demonstrate how to use the SerialPort control for
 *             communicating with your PC's Serial RS-232 COM Port
 * 
 *             It is for educational purposes only and not sanctified for industrial use. :)
 *             Written to support the blog post article at: http://msmvps.com/blogs/coad/archive/2005/03/23/39466.aspx
 * 
 *             Search for "comport" to see how I'm using the SerialPort control.
 */

#region Namespace Inclusions
using System;
using System.Linq;
using System.Text;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;

using SerialPortTerminal.Properties;
using System.Threading;
using System.IO;


#endregion

namespace SerialPortTerminal
{
  #region Public Enumerations
  public enum DataMode { Text, Hex }
  public enum LogMsgType { Incoming, Outgoing, Normal, Warning, Error };
  #endregion

  public partial class frmTerminal : Form
  {
    #region Local Variables


      byte[] buffer1420_test = new byte[1024];
      private int bytes1420_test = 0;


    private byte[] globalBuffer1420 = new byte[4096];
    private int parsingIndex1420 = 0;

    // The main control for communicating through the RS-232 port
    private SerialPort comport = new SerialPort();

    // Various colors for logging info
    private Color[] LogMsgTypeColor = { Color.Blue, Color.Green, Color.Black, Color.Orange, Color.Red };

    // Temp holder for whether a key was pressed
    private bool KeyHandled = false;

		private Settings settings = Settings.Default;

      static System.Windows.Forms.Timer valueUpTimer = new System.Windows.Forms.Timer();
      static System.Windows.Forms.Timer nodeStatusTimer = new System.Windows.Forms.Timer();
      
      private int nodeStatusButton = 0;

      private int valuUpButtonStatus = 0;

      private int timerValue = 0;
    #endregion

    #region Constructor
    public frmTerminal()
    {
			// Load user settings
			settings.Reload();

      // Build the form
      InitializeComponent();

      // Restore the users settings
      InitializeControlValues();

      // Enable/disable controls based on the current state
      EnableControls();

      // When data is recieved through the port, call this method
      comport.DataReceived += new SerialDataReceivedEventHandler(port_DataReceived);
			comport.PinChanged += new SerialPinChangedEventHandler(comport_PinChanged);
    }

		void comport_PinChanged(object sender, SerialPinChangedEventArgs e)
		{
			// Show the state of the pins
			UpdatePinState();
		}

		private void UpdatePinState()
		{
			this.Invoke(new ThreadStart(() => {
				// Show the state of the pins
				chkCD.Checked = comport.CDHolding;
				chkCTS.Checked = comport.CtsHolding;
				chkDSR.Checked = comport.DsrHolding;
			}));
		}
    #endregion

    #region Local Methods
    
    /// <summary> Save the user's settings. </summary>
    private void SaveSettings()
    {
			settings.BaudRate = int.Parse(cmbBaudRate.Text);
			settings.DataBits = int.Parse(cmbDataBits.Text);
			settings.DataMode = CurrentDataMode;
			settings.Parity = (Parity)Enum.Parse(typeof(Parity), cmbParity.Text);
			settings.StopBits = (StopBits)Enum.Parse(typeof(StopBits), cmbStopBits.Text);
			settings.PortName = cmbPortName.Text;
			settings.ClearOnOpen = chkClearOnOpen.Checked;
			settings.ClearWithDTR = chkClearWithDTR.Checked;

			settings.Save();
    }

    /// <summary> Populate the form's controls with default settings. </summary>
    private void InitializeControlValues()
    {

        timerValue = Convert.ToInt32(this.textBox_value.Text);
      cmbParity.Items.Clear(); cmbParity.Items.AddRange(Enum.GetNames(typeof(Parity)));
      cmbStopBits.Items.Clear(); cmbStopBits.Items.AddRange(Enum.GetNames(typeof(StopBits)));

			cmbParity.Text = settings.Parity.ToString();
			cmbStopBits.Text = settings.StopBits.ToString();
			cmbDataBits.Text = settings.DataBits.ToString();
			cmbParity.Text = settings.Parity.ToString();
			cmbBaudRate.Text = settings.BaudRate.ToString();
			CurrentDataMode = settings.DataMode;

			RefreshComPortList();

			chkClearOnOpen.Checked = settings.ClearOnOpen;
			chkClearWithDTR.Checked = settings.ClearWithDTR;

			// If it is still avalible, select the last com port used
			if (cmbPortName.Items.Contains(settings.PortName)) cmbPortName.Text = settings.PortName;
      else if (cmbPortName.Items.Count > 0) cmbPortName.SelectedIndex = cmbPortName.Items.Count - 1;
      else
      {
        MessageBox.Show(this, "There are no COM Ports detected on this computer.\nPlease install a COM Port and restart this app.", "No COM Ports Installed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        this.Close();
      }
    }

    /// <summary> Enable/disable controls based on the app's current state. </summary>
    private void EnableControls()
    {
      // Enable/disable controls based on whether the port is open or not
      gbPortSettings.Enabled = !comport.IsOpen;
      txtSendData.Enabled = btnSend.Enabled = comport.IsOpen;
			//chkDTR.Enabled = chkRTS.Enabled = comport.IsOpen;

      if (comport.IsOpen) btnOpenPort.Text = "&Close Port";
      else btnOpenPort.Text = "&Open Port";
    }

    /// <summary> Send the user's data currently entered in the 'send' box.</summary>
    private void SendData()
    {
      if (CurrentDataMode == DataMode.Text)
      {
        // Send the user's text straight out the port
        comport.Write(txtSendData.Text);

        // Show in the terminal window the user's text
        Log(LogMsgType.Outgoing, txtSendData.Text + "\n");
      }
      else
      {
        try
        {
          // Convert the user's string of hex digits (ex: B4 CA E2) to a byte array
          byte[] data = HexStringToByteArray(txtSendData.Text);

          // Send the binary data out the port
          comport.Write(data, 0, data.Length);

          // Show the hex digits on in the terminal window
          Log(LogMsgType.Outgoing, ByteArrayToHexString(data) + "\n");
        }
        catch (FormatException)
        {
          // Inform the user if the hex string was not properly formatted
          Log(LogMsgType.Error, "Not properly formatted hex string: " + txtSendData.Text + "\n");
        }
      }
      txtSendData.SelectAll();
    }

    /// <summary> Log data to the terminal window. </summary>
    /// <param name="msgtype"> The type of message to be written. </param>
    /// <param name="msg"> The string containing the message to be shown. </param>
    private void Log(LogMsgType msgtype, string msg)
    {
      rtfTerminal.Invoke(new EventHandler(delegate
      {
        rtfTerminal.SelectedText = string.Empty;
        rtfTerminal.SelectionFont = new Font(rtfTerminal.SelectionFont, FontStyle.Bold);
        rtfTerminal.SelectionColor = LogMsgTypeColor[(int)msgtype];
          //rtfTerminal.Focus();
          //rtfTerminal.SelectionStart += rtfTerminal.SelectionStart + 1;
          //rtfTerminal.SelectionLength = 10;
        if (rtfTerminal.Lines.Length > 1000)
        {
            rtfTerminal.Clear();
        }
        rtfTerminal.AppendText(msg+"\r\n");
        rtfTerminal.ScrollToCaret();
        
      }
      ));
    }

    /// <summary> Convert a string of hex digits (ex: E4 CA B2) to a byte array. </summary>
    /// <param name="s"> The string containing the hex digits (with or without spaces). </param>
    /// <returns> Returns an array of bytes. </returns>
    private byte[] HexStringToByteArray(string s)
    {
      s = s.Replace(" ", "");
      byte[] buffer = new byte[s.Length / 2];
      for (int i = 0; i < s.Length; i += 2)
        buffer[i / 2] = (byte)Convert.ToByte(s.Substring(i, 2), 16);
      return buffer;
    }

    /// <summary> Converts an array of bytes into a formatted string of hex digits (ex: E4 CA B2)</summary>
    /// <param name="data"> The array of bytes to be translated into a string of hex digits. </param>
    /// <returns> Returns a well formatted string of hex digits with spacing. </returns>
    private string ByteArrayToHexString(byte[] data)
    {
      StringBuilder sb = new StringBuilder(data.Length * 3);
      foreach (byte b in data)
        sb.Append(Convert.ToString(b, 16).PadLeft(2, '0').PadRight(3, ' '));
      return sb.ToString().ToUpper();
    }
    #endregion

    #region Local Properties
    private DataMode CurrentDataMode
    {
      get
      {
        if (rbHex.Checked) return DataMode.Hex;
        else return DataMode.Text;
      }
      set
      {
        if (value == DataMode.Text) rbText.Checked = true;
        else rbHex.Checked = true;
      }
    }
    #endregion

    #region Event Handlers
    private void lnkAbout_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
      // Show the user the about dialog
      (new frmAbout()).ShowDialog(this);
    }
    
    private void frmTerminal_Shown(object sender, EventArgs e)
    {
      Log(LogMsgType.Normal, String.Format("Application Started at {0}\n", DateTime.Now));
    }
    private void frmTerminal_FormClosing(object sender, FormClosingEventArgs e)
    {
      // The form is closing, save the user's preferences
      SaveSettings();
    }

    private void rbText_CheckedChanged(object sender, EventArgs e)
    { if (rbText.Checked) CurrentDataMode = DataMode.Text; }

    private void rbHex_CheckedChanged(object sender, EventArgs e)
    { if (rbHex.Checked) CurrentDataMode = DataMode.Hex; }

    private void cmbBaudRate_Validating(object sender, CancelEventArgs e)
    { int x; e.Cancel = !int.TryParse(cmbBaudRate.Text, out x); }

    private void cmbDataBits_Validating(object sender, CancelEventArgs e)
    { int x; e.Cancel = !int.TryParse(cmbDataBits.Text, out x); }

    private void btnOpenPort_Click(object sender, EventArgs e)
    {
			bool error = false;

      // If the port is open, close it.
      if (comport.IsOpen) comport.Close();
      else
      {
        // Set the port's settings
        comport.BaudRate = int.Parse(cmbBaudRate.Text);
        comport.DataBits = int.Parse(cmbDataBits.Text);
        comport.StopBits = (StopBits)Enum.Parse(typeof(StopBits), cmbStopBits.Text);
        comport.Parity = (Parity)Enum.Parse(typeof(Parity), cmbParity.Text);
        comport.PortName = cmbPortName.Text;

				try
				{
					// Open the port
					comport.Open();
				}
				catch (UnauthorizedAccessException) { error = true; }
				catch (IOException) { error = true; }
				catch (ArgumentException) { error = true; }

				if (error) MessageBox.Show(this, "Could not open the COM port.  Most likely it is already in use, has been removed, or is unavailable.", "COM Port Unavalible", MessageBoxButtons.OK, MessageBoxIcon.Stop);
				else
				{
					// Show the initial pin states
					UpdatePinState();
					chkDTR.Checked = comport.DtrEnable;
					chkRTS.Checked = comport.RtsEnable;
				}
      }

      // Change the state of the form's controls
      EnableControls();

      // If the port is open, send focus to the send data box
			if (comport.IsOpen)
			{
				txtSendData.Focus();
				if (chkClearOnOpen.Checked) ClearTerminal();
			}
    }
    private void btnSend_Click(object sender, EventArgs e)
    { SendData(); }

      //private void threadWithParam(byte[] buffer, int bytes)
      //{
          
      //}

    private void Parsing1420(byte[] buffer, int size)
    //public void Parsing1420()
    {
        //byte[] buffer = new byte[bytes1420_test];
        //int size = bytes1420_test;
        //Array.Copy(buffer1420_test, 0, buffer, 0, size);

        //Mutex mutex = new Mutex();
        //mutex.WaitOne();
        try
        {

            byte[] localBuffer1420 = new byte[4096];

            if (parsingIndex1420 >= 11)
            {
                

                byte[] sendBuffer1420 = new byte[11];

                parsingIndex1420 += size;
                do
                {
                    Array.Copy(globalBuffer1420, 0, sendBuffer1420, 0, 11);


                    //rtrnNodeInfo rtrn = new rtrnNodeInfo();

                    //rtrn = checkNodeLife(sendBuffer1420[2]);

                    if (sendBuffer1420[2] != 0 && sendBuffer1420[0] == 0x02 && sendBuffer1420[10] == 0x03)
                    {
                        //System.Threading.Thread.Sleep(100);
                        OnUpdatePacketView(sendBuffer1420);
                        //Thread aThread = new Thread(new ThreadStart(OnUpdatePacketView));
                    }
                        


                    parsingIndex1420 -= 11;
                    if (parsingIndex1420 != 0)
                    {

                        Array.Copy(globalBuffer1420, 0, localBuffer1420, 0, 4096);
                        Array.Clear(globalBuffer1420, 0, 4096);
                        Array.Copy(buffer, 0, globalBuffer1420, 0, parsingIndex1420);

                        
                    }
                    else
                        Array.Clear(globalBuffer1420, 0, 4096);

                } while (parsingIndex1420 >= 11);

                

            }
            else
            {
                Array.Copy(globalBuffer1420, 0, localBuffer1420, 0, 4096);
                Array.Clear(globalBuffer1420, 0, 4096);
                Array.Copy(buffer, 0, localBuffer1420, parsingIndex1420, size);

                parsingIndex1420 += size;
                Array.Copy(localBuffer1420, 0, globalBuffer1420, 0, parsingIndex1420);



            }

        }
        catch (Exception er)
        {
            MessageBox.Show(er.Message);
        }

        //mutex.ReleaseMutex();
    }

      public static void threadTest()
      {
          
      }
    private void port_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
			// If the com port has been closed, do nothing
			if (!comport.IsOpen) return;

      // This method will be called when there is data waiting in the port's buffer

      // Determain which mode (string or binary) the user is in
        /*
      if (CurrentDataMode == DataMode.Text)
      {
        // Read all the data waiting in the buffer
        string data = comport.ReadExisting();

        // Display the text to the user in the terminal
        Log(LogMsgType.Incoming, data);
      }
      else
         */
      {
        // Obtain the number of bytes waiting in the port's buffer
        int bytes = comport.BytesToRead;

        // Create a byte array buffer to hold the incoming data
        byte[] buffer = new byte[bytes];

        // Read the data from the port and store it in our buffer
        comport.Read(buffer, 0, bytes);

          //buffer1420_test
          //Array.Clear(buffer1420_test, 0, 1024);
          //Array.Copy(buffer, 0, buffer1420_test, 0, bytes);

          //bytes1420_test = bytes;
              
        
          //Parsing1420();
          //Thread aThread = new Thread(new ThreadStart(Parsing1420));
          //aThread.Start();
          //aThread.Join();
        // Show the user the incoming data in hex format
        Log(LogMsgType.Incoming, ByteArrayToHexString(buffer));

          byte[] sendbuffer = new byte[4096];
          Array.Copy(buffer, 0, sendbuffer, 0, bytes);
          Parsing1420(sendbuffer, bytes);

        
      }
    }
    public struct nodeIndexCheck
    {
        public int[] NodeID;
        public int[] index;
        public int[] flag;
        public int[] life;
        public int dept;


        public nodeIndexCheck(int p1, int p2, int p3, int p4)
        {
            NodeID = new int[p1];
            index = new int[p2];
            flag = new int[p3];
            life = new int[p4];
            dept = 0;

        }
    };

    nodeIndexCheck checkBox1420 = new nodeIndexCheck(256, 256, 256, 256);

    public struct rtrnNodeInfo
    {
        public int NodeID;
        public int index;
        public int flag;
    };

    private delegate void UpdatePacketView(byte[] data);

      
    public void OnUpdatePacketView(byte[] data)
    {
        try
        {
            //if (this.PacketlistView.InvokeRequired)
            //{
            //    this.PacketlistView.BeginInvoke(new UpdatePacketView(OnUpdatePacketView), data);
            //}
            //else
            //{
            
            this.PacketlistView.Invoke(new EventHandler(delegate
             {
                    String Group = String.Format("{0}", data[1]);
                    String NodeID = String.Format("{0}", data[2]);
                    String SeqNo = String.Format("{0}", data[3]);
                    String DI1 = String.Format("{0}", data[4]);
                    String DI2 = String.Format("{0}", data[5]);
                    String DI3 = String.Format("{0}", data[6]);
                    String DI4 = String.Format("{0}", data[7]);



                    rtrnNodeInfo rtrn = new rtrnNodeInfo();

                    rtrn = checkNodeLife(Int32.Parse(NodeID));

                 
                 if (rtrn.flag == 1)
                 {
                     //if (Int32.Parse(NodeID) == 31)
                     //    this.PacketlistView.Items[rtrn.index].BackColor = Color.Red;
                     //    //this.PacketlistView.Items[rtrn.index].ForeColor = Color.Red;
                     //if (Int32.Parse(NodeID) == 32)
                     //    this.PacketlistView.Items[rtrn.index].BackColor = Color.Yellow;

                     //if (Int32.Parse(NodeID) == 33)
                     //    this.PacketlistView.Items[rtrn.index].BackColor = Color.Green;

                     this.PacketlistView.Items[rtrn.index].SubItems[2].Text = SeqNo;
                     this.PacketlistView.Items[rtrn.index].SubItems[3].Text = DI1;
                     this.PacketlistView.Items[rtrn.index].SubItems[4].Text = DI2;
                     this.PacketlistView.Items[rtrn.index].SubItems[5].Text = DI3;
                     this.PacketlistView.Items[rtrn.index].SubItems[6].Text = DI4;

                     //this.PacketlistView.Invalidate();
                 }
                 else
                    {

                        this.PacketlistView.Items.Add(new ListViewItem(new string[]
                            {

                                Group, NodeID, SeqNo, DI1, DI2, DI3, DI4
                            }));
                    }
                 

                }
                ));

            //this.PacketlistView.Items[this.PacketlistView.Items.Count - 1].EnsureVisible();
            //this.PacketlistView.EndUpdate();
            //}

            

        }
        catch (Exception er)
        {
            MessageBox.Show(er.Message);
        }

    }

     
 

      public rtrnNodeInfo checkNodeLife(int nodeid)
      {
          rtrnNodeInfo rtrn = new rtrnNodeInfo();

          for (int i = 0; i < 256; i++)
          {
              if (checkBox1420.NodeID[i] == nodeid)
              {
                  checkBox1420.flag[i] = 1;
                  checkBox1420.life[i] = 1;

                  rtrn.index = checkBox1420.index[i];
                  rtrn.flag = 1;

                  return rtrn;
              }
              
          }

          // 새로운 노드일 경우
          checkBox1420.NodeID[checkBox1420.dept] = nodeid;
          checkBox1420.index[checkBox1420.dept] = checkBox1420.dept;
          checkBox1420.life[checkBox1420.dept] = 1;

          rtrn.index = checkBox1420.dept;
          rtrn.flag = 0;

          checkBox1420.dept++;


          return rtrn;
      }



    

    private void txtSendData_KeyDown(object sender, KeyEventArgs e)
    { 
      // If the user presses [ENTER], send the data now
      if (KeyHandled = e.KeyCode == Keys.Enter) { e.Handled = true; SendData(); } 
    }
    private void txtSendData_KeyPress(object sender, KeyPressEventArgs e)
    { e.Handled = KeyHandled; }
    #endregion

		private void chkDTR_CheckedChanged(object sender, EventArgs e)
		{
			comport.DtrEnable = chkDTR.Checked;
			if (chkDTR.Checked && chkClearWithDTR.Checked) ClearTerminal();
		}

		private void chkRTS_CheckedChanged(object sender, EventArgs e)
		{
			comport.RtsEnable = chkRTS.Checked;
		}

		private void btnClear_Click(object sender, EventArgs e)
		{
			ClearTerminal();
		}

      private void ClearTerminal()
      {
          rtfTerminal.Clear();
          this.PacketlistView.Items.Clear();


          for (int i = 0; i < 32; i++)
          {
              checkBox1420.NodeID[i] = 0;
              checkBox1420.index[i] = 0;
              checkBox1420.flag[i] = 0;
              checkBox1420.dept = 0;
          }
      }

      private void tmrCheckComPorts_Tick(object sender, EventArgs e)
		{
			// checks to see if COM ports have been added or removed
			// since it is quite common now with USB-to-Serial adapters
			RefreshComPortList();
		}

		private void RefreshComPortList()
		{
			// Determain if the list of com port names has changed since last checked
			string selected = RefreshComPortList(cmbPortName.Items.Cast<string>(), cmbPortName.SelectedItem as string, comport.IsOpen);

			// If there was an update, then update the control showing the user the list of port names
			if (!String.IsNullOrEmpty(selected))
			{
				cmbPortName.Items.Clear();
				cmbPortName.Items.AddRange(OrderedPortNames());
				cmbPortName.SelectedItem = selected;
			}
		}

		private string[] OrderedPortNames()
		{
			// Just a placeholder for a successful parsing of a string to an integer
			int num;

			// Order the serial port names in numberic order (if possible)
			return SerialPort.GetPortNames().OrderBy(a => a.Length > 3 && int.TryParse(a.Substring(3), out num) ? num : 0).ToArray(); 
		}
		
		private string RefreshComPortList(IEnumerable<string> PreviousPortNames, string CurrentSelection, bool PortOpen)
		{
			// Create a new return report to populate
			string selected = null;

			// Retrieve the list of ports currently mounted by the operating system (sorted by name)
			string[] ports = SerialPort.GetPortNames();

			// First determain if there was a change (any additions or removals)
			bool updated = PreviousPortNames.Except(ports).Count() > 0 || ports.Except(PreviousPortNames).Count() > 0;

			// If there was a change, then select an appropriate default port
			if (updated)
			{
				// Use the correctly ordered set of port names
				ports = OrderedPortNames();

				// Find newest port if one or more were added
				string newest = SerialPort.GetPortNames().Except(PreviousPortNames).OrderBy(a => a).LastOrDefault();

				// If the port was already open... (see logic notes and reasoning in Notes.txt)
				if (PortOpen)
				{
					if (ports.Contains(CurrentSelection)) selected = CurrentSelection;
					else if (!String.IsNullOrEmpty(newest)) selected = newest;
					else selected = ports.LastOrDefault();
				}
				else
				{
					if (!String.IsNullOrEmpty(newest)) selected = newest;
					else if (ports.Contains(CurrentSelection)) selected = CurrentSelection;
					else selected = ports.LastOrDefault();
				}
			}

			// If there was a change to the port list, return the recommended default selection
			return selected;
		}

        private void button_valueup_interval_Click(object sender, EventArgs e)
        {
            if (!comport.IsOpen)
            {
                MessageBox.Show("시리얼 포트를 열어주세요.");
                return;
            }
                

            switch (valuUpButtonStatus)
            {
                case 0:
                    //valueUpTimer_Tick(sender, e);

                    timerValue = Convert.ToInt32(this.textBox_value.Text);
                    valueUpTimer.Tick += new EventHandler(valueUpTimer_Tick);
                    valueUpTimer.Interval = Convert.ToInt32(this.textBox_valueup_interval.Text);
                    valueUpTimer.Start();
                    valuUpButtonStatus = 1;
                    button_valueup_interval.Text = "Stop";
                    this.textBox_valueup_interval.Enabled = false;
                    break;
                case 1:
                    valueUpTimer.Stop();
                    valuUpButtonStatus = 0;
                    button_valueup_interval.Text = "Start";
                    this.textBox_valueup_interval.Enabled = true;
                    break;
            }
            
        }

        delegate void SetTextBox_valueCallback(string text);

        private void SetTextBox_value(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.textBox_value.InvokeRequired)
            {
                SetTextBox_valueCallback d = new SetTextBox_valueCallback(SetTextBox_value);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.textBox_value.Text = text;
            }
        }
      private void nodeStatusTimer_Tick(object sender, EventArgs e)
      {

          for (int i = 0; i < checkBox1420.dept; i++)
          {
              if (checkBox1420.life[i] == 0)
              {
                  OnUpdateNodeStatus(checkBox1420.index[i], 0);
                  //if (Int32.Parse(NodeID) == 31)
                  //    this.PacketlistView.Items[rtrn.index].BackColor = Color.Red;
              }
              else
              {
                  OnUpdateNodeStatus(checkBox1420.index[i], 1);
              }

              checkBox1420.life[i] = 0;

          }
      }

        private void valueUpTimer_Tick(object sender, EventArgs e)
        {
            string value;

            value = string.Format("{0:000000}", timerValue);

            int bytes = 6;
            byte[] buffer = new byte[bytes];
            byte[] sendBuffer = new byte[bytes+2];
            int i = 0;

            timerValue++;

            if (!comport.IsOpen)
            {
                
                Log(LogMsgType.Outgoing, System.DateTime.Now + " - " + "시리얼 포트 닫힘.\n");
                valueUpTimer.Stop();
                valuUpButtonStatus = 0;
                button_valueup_interval.Text = "Start";
                this.textBox_valueup_interval.Enabled = true;
                return;
            }

            try
            {
                
                buffer = System.Text.Encoding.Default.GetBytes(string.Format("{0:000000}", timerValue));
                sendBuffer[i] = 0x02;
                for (i = 0; i < value.Length; i++ )
                    sendBuffer[i+1] = buffer[i];

                sendBuffer[i+1] = 0x03;

                //comport.Write(Encoding.Default.GetString(sendBuffer));
                comport.Write(value);
                this.SetTextBox_value(value);

                

                //this.textBox_value.Text = Convert.ToString(timerValue);
                // Show in the terminal window the user's text
                Log(LogMsgType.Outgoing, System.DateTime.Now + " - " + value + "\n");    
            }
            catch (Exception er)
            {
                MessageBox.Show(er.Message);
            }
  
            //textBox1.Text = "";
            //foreach (System.Diagnostics.Process procName in System.Diagnostics.Process.GetProcesses())
            //{
            //    string procNm = procName.ToString();
            //    procNm = procNm.Replace("System.Diagnostics.Process (", "");
            //    procNm = procNm.Replace(")", "");
            //    textBox1.AppendText(procNm + Environment.NewLine);
            //}
        }
        public void OnUpdateNodeStatus(int index, int life)
    {
            try
            {
   

                this.PacketlistView.Invoke(new EventHandler(delegate
                    {
                        if( life == 1 )
                            this.PacketlistView.Items[index].BackColor = Color.White;
                        else
                        this.PacketlistView.Items[index].BackColor = Color.Red;

                    }));
            }
            catch (Exception er)
            {
                MessageBox.Show(er.Message);
            }

    }


            private
            void button_nodecheck_interval_Click(object sender, EventArgs e)
        {
            if (!comport.IsOpen)
            {
                MessageBox.Show("시리얼 포트를 열어주세요.");
                return;
            }

    

            switch (nodeStatusButton)
            {
                case 0:
                    //valueUpTimer_Tick(sender, e);

                    //timerValue = Convert.ToInt32(this.textBox_value.Text);
                    nodeStatusTimer.Tick += new EventHandler(nodeStatusTimer_Tick);
                    nodeStatusTimer.Interval = Convert.ToInt32(this.textBox_nodecheck_interval.Text);
                    nodeStatusTimer.Start();
                    nodeStatusButton = 1;
                    button_nodecheck_interval.Text = "Stop";
                    this.textBox_nodecheck_interval.Enabled = false;
                    break;
                case 1:
                    nodeStatusTimer.Stop();
                    nodeStatusButton = 0;
                    button_nodecheck_interval.Text = "Start";
                    this.textBox_nodecheck_interval.Enabled = true;
                    break;
            }
            
        }
	}
}