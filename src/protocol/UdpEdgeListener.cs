/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2005  University of California
Copyright (C) 2005,2006  P. Oscar Boykin <boykin@pobox.com>, University of Florida

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

/*
 * Dependencies : 
 * Brunet.Edge
 * Brunet.EdgeException
 * Brunet.EdgeListener;
 * Brunet.Packet;
 * Brunet.PacketParser;
 * Brunet.TransportAddress;
 * Brunet.UdpEdge;
 */

using Brunet;
using System;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Collections;

namespace Brunet
{
  /**
   * There are multiple implementations of Udp transports for
   * Brunet.  This is a base class with the shared code.
   */
  public abstract class UdpEdgeListenerBase : EdgeListener
  {
    ///Buffer to read the packets into
    protected byte[] _rec_buffer;
    protected byte[] _send_buffer;

    ///Here is the queue for outgoing packets:
    protected Queue _send_queue;
    //This is true if there is something in the queue
    protected bool _queue_not_empty;
    /**
     * This is a simple little class just to hold the
     * two objects needed to do a send
     */
    protected class SendQueueEntry {
      public SendQueueEntry(Packet p, UdpEdge udpe) {
        Packet = p;
        Sender = udpe;
      }
      public Packet Packet;
      public UdpEdge Sender;
    }

    /*
     * This is the object which we pass to UdpEdges when we create them.
     */
    protected IPacketHandler _send_handler;
    /**
     * Hashtable of ID to Edges
     */
    protected Hashtable _id_ht;
    protected Hashtable _remote_id_ht;

    protected Random _rand;

    protected TAAuthorizer _ta_auth;
    protected ArrayList _tas;
    public override ArrayList LocalTAs
    {
      get
      {
        return ArrayList.ReadOnly(_tas);
      }
    }

    public override TransportAddress.TAType TAType
    {
      get
      {
        return TransportAddress.TAType.Udp;
      }
    }
    
    ///used for thread for the socket synchronization
    protected object _sync;
    
    protected bool _running;
    protected bool _isstarted;
    public override bool IsStarted
    {
      get { return _isstarted; }
    }

    //This is our best guess of the local endpoint
    protected IPEndPoint _local_ep;
    
    protected enum ControlCode : int
    {
      EdgeClosed = 1
    }
    
    /**
     * When a UdpEdge closes we need to remove it from
     * our table, so we will know it is new if it comes
     * back.
     */
    public void CloseHandler(object edge, EventArgs args)
    {
      UdpEdge e = (UdpEdge)edge;
      lock( _id_ht ) {
        _id_ht.Remove( e.ID );
	object re = _remote_id_ht[ e.RemoteID ];
	if( re == e ) {
          //_remote_id_ht only keeps track of incoming edges,
	  //so, there could be two edges with the same remoteid
	  //that are not equivalent.
	  _remote_id_ht.Remove( e.RemoteID );
	}
      }
    }
   
    protected IPEndPoint GuessLocalEndPoint(IEnumerable tas) {
      IPAddress ipa = IPAddress.Loopback;
      bool stop = false;
      int port = 0;
      foreach(TransportAddress ta in tas) {
        ArrayList ips = ta.GetIPAddresses();
        port = ta.Port;
	foreach(IPAddress ip in ips) {
          if( !IPAddress.IsLoopback(ip) && (ip.Address != 0) ) {
		  //0 is the 0.0.0.0, or any address
            ipa = ip;
	    stop = true;
	    break;
	  }
	}
	if( stop ) { break; }
      }
      //ipa, now holds our best guess for an endpoint..
      return new IPEndPoint(ipa, port);
    }
    /**
     * This handles lightweight control messages that may be sent
     * by UDP
     */
    protected void HandleControlPacket(int remoteid, int n_localid, byte[] buffer,
                                       object state)
    {
      int local_id = ~n_localid;
      //Reading from a hashtable is treadsafe
      UdpEdge e = (UdpEdge)_id_ht[local_id];
      if( (e != null) && (e.RemoteID == remoteid) ) {
        //This edge has some control information, the information starts at byte 8.
        try {
	  ControlCode code = (ControlCode)NumberSerializer.ReadInt(buffer, 8);
          System.Console.WriteLine("Got control from: {0}", e);
	  if( code == ControlCode.EdgeClosed ) {
            //The edge has been closed on the other side
	    e.Close();
 	  }
        }
        catch(Exception x) {
        //This could happen if this is some control message we don't understand
          Console.Error.WriteLine(x);
        }
      }
    }

    /**
     * This reads a packet from buf which came from end, with
     * the given ids
     */
    protected void HandleDataPacket(int remoteid, int localid,
                                    byte[] buf, int off, int len,
                                    EndPoint end, object state)
    {
      bool read_packet = true;
      bool is_new_edge = false;
      //It is threadsafe to read from Hashtable
      UdpEdge edge = (UdpEdge)_id_ht[localid];
      if( localid == 0 ) {
        //This is a potentially a new incoming edge
        is_new_edge = true;

        //Check to see if it is a dup:
        UdpEdge e_dup = (UdpEdge)_remote_id_ht[remoteid];
        if( e_dup != null ) {
          //Lets check to see if this is a true dup:
          if( e_dup.End.Equals( end ) ) {
            //Same id from the same endpoint, looks like a dup...
            is_new_edge = false;
            //Console.WriteLine("Stopped a Dup on: {0}", e_dup);
            //Reuse the existing edge:
            edge = e_dup;
          }
          else {
            //This is just a coincidence.
          }
        }
        if( is_new_edge ) {
          TransportAddress rta = new TransportAddress(this.TAType,(IPEndPoint)end);
          if( _ta_auth.Authorize(rta) == TAAuthorizer.Decision.Deny ) {
            //This is bad news... Ignore it...
            ///@todo perhaps we should send a control message... I don't know
            is_new_edge= false;
            read_packet = false;
            Console.Error.WriteLine("Denying: {0}", rta);
          }
          else {
            //We need to assign it a local ID:
            lock( _id_ht ) {
              /*
               * Now we need to lock the table so that it cannot
               * be written to by anyone else while we work
               */
              do {
                localid = _rand.Next();
                //Make sure not to use negative ids
                if( localid < 0 ) { localid = ~localid; }
              } while( _id_ht.Contains(localid) || localid == 0 );
              /*
               * We copy the endpoint because (I think) .Net
               * overwrites it each time.  Since making new
               * edges is rare, this is better than allocating
               * a new endpoint each time
               */
              IPEndPoint this_end = (IPEndPoint)end;
              IPEndPoint my_end = new IPEndPoint(this_end.Address,
                                                 this_end.Port);
              edge = new UdpEdge(_send_handler, true, my_end,
                             _local_ep, localid, remoteid);
              _id_ht[localid] = edge;
              _remote_id_ht[remoteid] = edge;
            }
            edge.CloseEvent += new EventHandler(this.CloseHandler);
          }
        }
      }
      else if ( edge == null ) {
        /*
         * This is the case where the Edge is not a new edge,
         * but we don't know about it.  It is probably an old edge
         * that we have closed.  We can ignore this packet
         */
        read_packet = false;
	 //Send a control packet
        SendControlPacket(end, remoteid, localid, ControlCode.EdgeClosed, state);
      }
      else if ( edge.RemoteID == 0 ) {
        /* This is the response to our edge creation */
        edge.RemoteID = remoteid;
      }
      else if( edge.RemoteID != remoteid ) {
        /*
         * This could happen as a result of packet loss or duplication
         * on the first packet.  We should ignore any packet that
         * does not have both ids matching.
         */
        read_packet = false;
	 //Tell the other guy to close this ignored edge
        SendControlPacket(end, remoteid, localid, ControlCode.EdgeClosed, state);
        edge = null;
      }
      if( (edge != null) && !edge.End.Equals(end) ) {
        //This happens when a NAT mapping changes
        System.Console.WriteLine(
	    "NAT Mapping changed on Edge: {0}\n{1} -> {2}",
           edge, edge.End, end); 
           edge.End = end;
      }
      if( is_new_edge ) {
       SendEdgeEvent(edge);
      }
      if( read_packet ) {
        try {
          Packet p = PacketParser.Parse(buf, off, len);
          //We have the edge, now tell the edge to announce the packet:
          edge.Push(p);
        }
        catch(ParseException pe) {
          System.Console.Error.WriteLine(
             "Edge: {0} sent us an unparsable packet: {1}", edge, pe);
	}
      }
    }

    /**
     * Each implementation may have its own way of doing this
     */
    protected abstract void SendControlPacket(EndPoint end, int remoteid, int localid,
                                     ControlCode c, object state);

  }

  /**
  * A EdgeListener that uses UDP for the underlying
  * protocol.  This listener creates UDP edges.
  * 
  * The UdpEdgeListener creates one thread.  In that
  * thread it loops processing reads.  The UdpEdgeListener
  * keeps a Queue of packets to send also.  After each
  * read attempt, it sends all the packets in the Queue.
  *
  */

  public class UdpEdgeListener : UdpEdgeListenerBase, IPacketHandler
  {

    protected IPEndPoint ipep;
    protected Socket _s;

    ///this is the thread were the socket is read:
    protected Thread _thread;

    public UdpEdgeListener(int port):this(port, null)
    {
      
    }
    public UdpEdgeListener(int port, IPAddress[] ipList)
       : this(port, ipList, null)  { }
    /**
     * @param port the port to listen on
     * @param ipList the list of local IPAddresses to advertise
     * @param ta_auth the TAAuthorizer for outgoing and incoming TransportAddresses
     */
    public UdpEdgeListener(int port, IPAddress[] ipList, TAAuthorizer ta_auth)
    {
      /**
       * We get all the IPAddresses for this computer
       */
      _tas = GetIPTAs(TransportAddress.TAType.Udp, port, ipList);
      _local_ep = GuessLocalEndPoint(_tas); 
      _ta_auth = ta_auth;
      if( _ta_auth == null ) {
        //Always authorize in this case:
        _ta_auth = new ConstantAuthorizer(TAAuthorizer.Decision.Allow);
      }
      /*
       * Use this to listen for data
       */
      ipep = new IPEndPoint(IPAddress.Any, port);
      //We start out expecting around 30 edges with
      //a load factor of 0.15 (to make edge lookup fast)
      _id_ht = new Hashtable(30, 0.15f);
      _remote_id_ht = new Hashtable();
      _sync = new object();
      _running = false;
      _isstarted = false;
      //There are two 4 byte IDs for each edge we need to make room for
      _rec_buffer = new byte[ 8 + Packet.MaxLength ];
      _send_buffer = new byte[ 8 + Packet.MaxLength ];
      _send_queue = new Queue();
      _queue_not_empty = false;
      ///@todo, we need a system for using the cryographic RNG
      _rand = new Random();
      _send_handler = this;
    }

    /**
     * Implements the EdgeListener function to 
     * create edges of this type.
     */
    public override void CreateEdgeTo(TransportAddress ta, EdgeCreationCallback ecb)
    {
      if( !IsStarted )
      {
        ecb(false, null,
            new EdgeException("UdpEdgeListener is not started") );
      }
      else if( ta.TransportAddressType != this.TAType ) {
        ecb(false, null,
            new EdgeException(ta.TransportAddressType.ToString()
                              + " is not my type: " + this.TAType.ToString() ) );
      }
      else if( _ta_auth.Authorize(ta) == TAAuthorizer.Decision.Deny ) {
        //Too bad.  Can't make this edge:
        ecb(false, null,
            new EdgeException( ta.ToString() + " is not authorized") );
      }
      else {
        Edge e = null;
        ArrayList ip_addresses = ta.GetIPAddresses();
        IPAddress first_ip = (IPAddress)ip_addresses[0];
  
        IPEndPoint end = new IPEndPoint(first_ip, ta.Port);
        /* We have to keep our mapping of end point to edges up to date */
        lock( _id_ht ) {
          //Get a random ID for this edge:
          int id;
          do {
            id = _rand.Next();
  	  //Make sure we don't have negative ids
  	  if( id < 0 ) { id = ~id; }
          } while( _id_ht.Contains(id) || id == 0 );
          e = new UdpEdge(this, false, end, _local_ep, id, 0);
          _id_ht[id] = e;
        }
        /* Tell me when you close so I can clean up the table */
        e.CloseEvent += new EventHandler(this.CloseHandler);
        ecb(true, e, null);
      }
    }
   
    protected override void SendControlPacket(EndPoint end, int remoteid, int localid,
                                     ControlCode c, object state)
    {
        Socket s = (Socket)state;
        NumberSerializer.WriteInt(localid, _send_buffer, 0);
        //Bit flip to indicate this is a control packet
        NumberSerializer.WriteInt(~remoteid, _send_buffer, 4);
        NumberSerializer.WriteInt((int)c, _send_buffer, 8);

        try {	//catching SocketException
          s.SendTo(_send_buffer, 0, 12, SocketFlags.None, end);
          System.Console.WriteLine("Sending control to: {0}", end);
        }
        catch (SocketException sc) {
          Console.Error.WriteLine(
            "Error in Socket.SendTo. Endpoint: {0}\n{1}", end, sc);
        }
    }
    /**
     * This method may be called once to start listening.
     * @throw Exception if start is called more than once (including
     * after a Stop
     */
    public override void Start()
    {
      lock( _sync ) {
        if( _isstarted ) {
          //We can't start twice... too bad, so sad:
          throw new Exception("Restart never allowed");
        }
        _s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _s.Bind(ipep);
        _isstarted = true;
        _running = true;
      }
      _thread = new Thread( new ThreadStart(this.SocketThread) );
      _thread.Start();
    }

    /**
     * To stop listening, this method is called
     */
    public override void Stop()
    {
      _running = false;
    }

    /**
     * This is a System.Threading.ThreadStart delegate
     * We loop waiting for edges that need to send,
     * or data on the socket.
     *
     * This is the only thread that can touch the socket,
     * therefore, we do not need to lock the socket.
     */
    protected void SocketThread() // error happening here
    {
      //Wait 1 ms before giving up on a read
      int microsecond_timeout = 1000;
      //Make sure only this thread can see the socket from now on!
      Socket s = null;
      lock( _sync ) { 
        s = _s;
        _s = null;
      }
      EndPoint end = new IPEndPoint(IPAddress.Any, 0);
      while(_running) {
        bool read = false;

        /**
         * Note that at no time do we hold two locks, or
         * do we hold a lock across an external function call or event
         */
        //Read if we can:
        int rec_bytes = 0;
        //this is the only thread that can touch the socket!!!!!
        //otherwise we must lock!!!
        try {
          read = s.Poll( microsecond_timeout, SelectMode.SelectRead );
          if( read ) {
            rec_bytes = s.ReceiveFrom(_rec_buffer, ref end);
            //Get the id of this edge:
            int remoteid = NumberSerializer.ReadInt(_rec_buffer, 0);
            int localid = NumberSerializer.ReadInt(_rec_buffer, 4);
  	    if( localid < 0 ) {
  	    /*
  	     * We never give out negative id's, so if we got one
  	     * back the other node must be sending us a control
  	     * message.
  	     */
              HandleControlPacket(remoteid, localid, _rec_buffer, s);
  	    }
  	    else {
  	      HandleDataPacket(remoteid, localid, _rec_buffer, 8,
                               rec_bytes - 8, end, s);
  	    }
          }
        }
        catch(Exception x) {
          //Possible socket error. Just ignore the packet.
          Console.Error.WriteLine(x);
        }
        /*
         * We are done with handling the reads.  Now lets
         * deal with all the pending sends:
         *
         * Note, we don't get a lock before checking the queue.
         * There is no race condition or deadlock here because
         * if we don't get the packets this round, we get them
         * next time.  Getting locks is expensive, so we don't 
         * want to do it here since we don't have to, and this
         * is a tight loop.
         */
        if( _queue_not_empty ) {
          lock( _send_queue ) {
            bool more_to_send = false;
            int count;
            do {
              count = _send_queue.Count;
              if( count > 0 ) {
                SendQueueEntry sqe = (SendQueueEntry)_send_queue.Dequeue();
                Send(sqe, s);
              }
              //We sent exactly one, so if there was more than one, there is more to send
              more_to_send = count > 1;
            } while( more_to_send );
            //Before we unlock the send_queue, reset the flag:
            _queue_not_empty = false;
          }
        }
        //Now it is time to see if we can read...
      }
      lock( _sync ) {
        s.Close();
      }
    }

    private void Send(SendQueueEntry sqe, Socket s)
    {
      //We have a packet to send
      Packet p = sqe.Packet;
      UdpEdge sender = sqe.Sender;
      EndPoint e = sender.End;
      //Write the IDs of the edge:
      //[local id 4 bytes][remote id 4 bytes][packet]
      NumberSerializer.WriteInt(sender.ID, _send_buffer, 0);
      NumberSerializer.WriteInt(sender.RemoteID, _send_buffer, 4);
      p.CopyTo(_send_buffer, 8);
	      
      try {	//catching SocketException
        s.SendTo(_send_buffer, 0, 8 + p.Length, SocketFlags.None, e);
      }
      catch (SocketException sc) {
        Console.Error.WriteLine("Error in Socket send. Edge: {0}\n{1}", sender, sc);
      }
    }

    /**
     * When UdpEdge objects call Send, it calls this packet
     * callback:
     */
    public void HandlePacket(Packet p, Edge from)
    {
      lock( _send_queue ) {
        SendQueueEntry sqe = new SendQueueEntry(p, (UdpEdge)from);
        _send_queue.Enqueue(sqe);
        _queue_not_empty = true;
      }
    }

  }
}
