/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2005  University of California

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

using System;
using System.Collections;
using System.Net;

#if BRUNET_NUNIT
using System.Collections.Specialized;
using NUnit.Framework;
#endif


namespace Brunet
{

  /**
   * Represents the addresses used to transport the Brunet
   * protocol over lower layers (such as IP).  The transport
   * address is used when one host wants to connect to another
   * host in order to route Brunet packets.
   */

  public class TransportAddressFactory {
    //adding some kind of factory methods
    public static TransportAddress CreateInstance(string s) {
      string scheme = s.Substring(0, s.IndexOf(":"));
      string t = scheme.Substring(scheme.IndexOf('.') + 1);
      //Console.Error.WriteLine(t);
      TransportAddress.TAType ta_type = StringToType(t);
      
      if (ta_type ==  TransportAddress.TAType.Tcp) {
	return new IPTransportAddress(s);
      }
      if (ta_type ==  TransportAddress.TAType.Udp) {
	return new IPTransportAddress(s);
      }
      if (ta_type ==  TransportAddress.TAType.Function) {
	return new IPTransportAddress(s);
      }
      if (ta_type ==  TransportAddress.TAType.Tls) {
	return new IPTransportAddress(s);
      }
      if (ta_type ==  TransportAddress.TAType.TlsTest) {
	return new IPTransportAddress(s);
      }
      if (ta_type ==  TransportAddress.TAType.Tunnel) {
	return new TunnelTransportAddress(s);
      }
      return null;
    }
    
    protected static Hashtable _string_to_type = new Hashtable();

    public static TransportAddress.TAType StringToType(string s) {
      lock( _string_to_type ) {
        object t = _string_to_type[s];
        if( t == null ) {
          t = System.Enum.Parse(typeof(TransportAddress.TAType), s, true);
          _string_to_type[s] = t;
        }
        return (TransportAddress.TAType)t;
      }
    }

    public static TransportAddress CreateInstance(TransportAddress.TAType t,
						  string host, int port) {
      
      return new IPTransportAddress(t, host, port);
    }
    public static TransportAddress CreateInstance(TransportAddress.TAType t,
                            System.Net.IPAddress add, int port) {
      return new IPTransportAddress(t, add, port);
    }

    public static TransportAddress CreateInstance(TransportAddress.TAType t,
				   System.Net.IPEndPoint ep) {
      return new IPTransportAddress(t, ep);
    }

    protected class IPTransportEnum : IEnumerable {
      TransportAddress.TAType _tat;
      int _port;
      IEnumerable _ips;

      public IPTransportEnum(TransportAddress.TAType tat, int port, IEnumerable ips) {
        _tat = tat;
        _port = port;
        _ips = ips;
      }

      public IEnumerator GetEnumerator() {
        foreach(IPAddress ip in _ips) {  
          yield return new IPTransportAddress(_tat, new IPEndPoint(ip, _port) );  
        }
      }
    }

    /**
     * Creates an IEnumerable of TransportAddresses for a fixed type and port,
     * over a list of IPAddress objects.
     * Each time this the result is enumerated, ips.GetEnumerator is called,
     * so, if it changes, that is okay, (this is like a map() over a list, and
     * the original list can change).
     */
    public static IEnumerable Create(TransportAddress.TAType tat, int port, IEnumerable ips)
    {
      return new IPTransportEnum(tat, port, ips);
    }
    
    /**
     * This gets the name of the local machine, then does a DNS lookup on that
     * name, and finally does the same as TransportAddress.Create for that
     * list of IPAddress objects.
     *
     * If the DNS hostname is not correctly configured, it will return the
     * loopback address.
     */
    public static IEnumerable CreateForLocalHost(TransportAddress.TAType tat, int port) {
      try {
        string StrLocalHost = Dns.GetHostName();
        IPHostEntry IPEntry = Dns.GetHostByName(StrLocalHost);
        return Create(tat, port, IPEntry.AddressList);
      }
      catch(Exception) {
        //Oh, well, that didn't work.
        ArrayList tas = new ArrayList();
        //Just put the loopback address, it might help us talk to some other
        //local node.
        tas.Add( new IPTransportAddress(tat, new IPEndPoint(IPAddress.Loopback, port) ) );
        return tas;
      }
    }    
  }

  public abstract class TransportAddress:IComparable
  {
    
    protected string _scheme;

    public enum TAType
    {
      Unknown,
      Tcp,
      Udp,
      Function,
      Tls,
      TlsTest,
      Tunnel,
    }
    protected TransportAddress(string s) {
      _scheme = s;
    }

    public abstract TAType TransportAddressType { get;}


    public int CompareTo(object ta)
    {
      if (ta is TransportAddress) {
        ///@todo it would be nice to do a comparison that is not string based:
        return this.ToString().CompareTo(ta.ToString());
      }
      else {
        return -1;
      }
    }
  }

  public class IPTransportAddress: TransportAddress {
    protected ArrayList _ips = null;
    protected System.Uri _uri = null;
    
    public string Host {
      get {
	return _uri.Host;
      }
    }
    public int Port {
      get {
	return _uri.Port;
      }
    }
    protected TAType _type = TAType.Unknown;

    public override TAType TransportAddressType
    {
      get {
        if( _type == TAType.Unknown ) {
          string t = _uri.Scheme.Substring(_uri.Scheme.IndexOf('.') + 1);
          _type = TransportAddressFactory.StringToType(t);
        }
        return _type;
      }
    }
    public override string ToString() {
      return _uri.ToString();
    }
    public override bool Equals(object o) {
      if ( o == this ) { return true; }
      IPTransportAddress other = o as IPTransportAddress;
      if ( other == null ) { return false; }
      return _uri.Equals( other._uri );  
    }
    public override int GetHashCode() {
      return _uri.GetHashCode();
    }
    public IPTransportAddress(string uri):base(uri) { 
      _uri = new Uri(uri);
      _ips = null;
    }
    public IPTransportAddress(TransportAddress.TAType t,
                            string host, int port):
      this("brunet." + t.ToString().ToLower() + "://"
	   + host + ":" + port.ToString())
    {
      _ips = null;
    }
    public IPTransportAddress(TransportAddress.TAType t,
                            System.Net.IPAddress add, int port):
          this("brunet." + t.ToString().ToLower() + "://"
         + add.ToString() + ":" + port.ToString())
    {
      _ips = new ArrayList();
      _ips.Add( add );
    }
    public IPTransportAddress(TransportAddress.TAType t,
                            System.Net.IPEndPoint ep) :
      this(t, ep.Address, ep.Port) {
    }

    public ArrayList GetIPAddresses()
    {
      if ( _ips != null ) {
        return _ips;
      }

      try {
        IPAddress a = IPAddress.Parse(_uri.Host);
        _ips = new ArrayList();
        _ips.Add(a);
        return _ips;
      }
      catch(Exception) {

      }

      try {
        IPHostEntry IPHost = Dns.Resolve(_uri.Host);
        _ips = new ArrayList(IPHost.AddressList);
      } catch(Exception e) {
        // log this exception!
	System.Console.Error.WriteLine("In GetIPAddress() Resolving {1}: {0}",
                                        e, _uri.Host);
      }
      return _ips;
    }

  }
  public class TunnelTransportAddress: TransportAddress {
    protected Address _target;
    public Address Target {
      get {
	return _target;
      }
    }
    //in this new implementation, we have more than one packer forwarders
    protected ArrayList _forwarders;

    public TunnelTransportAddress(string s):base(s) {
      /** String representing the tunnel TA is as follows: brunet.tunnel://A/X1+X2+X3
       *  A: target address
       *  X1, X2, X3: forwarders, each X1, X2 and X3 is actually a slice of the initial few bytes of the address.
       */
      int k = s.IndexOf(":") + 3;
      int k1 = s.IndexOf("/", k);
      byte []addr_t  = Base32.Decode(s.Substring(k, k1 - k)); 
      _target = new AHAddress(addr_t);
      k = k1 + 1;
      _forwarders = new ArrayList();
      while (k < s.Length) {
	byte [] addr_prefix = Base32.Decode(s.Substring(k, 8));
	_forwarders.Add(MemBlock.Copy(addr_prefix));
	//jump over the 8 characters and the + sign
	k = k + 9;
      }
    }

    public TunnelTransportAddress(Address target, ArrayList forwarders): 
      this(GetString(target, forwarders)) 
    {
    }

    private static string GetString(Address target, ArrayList forwarders) {
      string s = "brunet.tunnel://" +  
	target.ToString().Substring(12) + "/";
      for (int idx = 0; idx < forwarders.Count; idx++) {
	Address a = (Address) forwarders[idx];
	s +=  a.ToString().Substring(12,8);
	if (idx < forwarders.Count - 1) {
	  //not the last element
	  s = s + "+";
	}      
      }
      return s;
    }
    public override TAType TransportAddressType { 
      get {
	return TransportAddress.TAType.Tunnel;
      }
    }
    public override string ToString() {
      return _scheme;
    }
    public override bool Equals(object o) {
      if ( o == this ) { return true; }
      TunnelTransportAddress other = o as TunnelTransportAddress;
      if ( other == null ) { return false; }
      return (TransportAddressType == other.TransportAddressType && 
	      _target.Equals(other._target));
      //&& 
      //_forwarder.Equals(other._forwarder));
    }

    public bool ContainsForwarder(Address addr) {
      MemBlock test_mem = MemBlock.Reference(Base32.Decode(addr.ToString().Substring(12, 8)));
      if (_forwarders.Contains(test_mem)) {
	return true;
      } 

      return false;
    }

    public override int GetHashCode() {
      return base.GetHashCode();
    }
  }
#if BRUNET_NUNIT

  [TestFixture]
  public class TATester {
    [Test]
    public void Test() {
      TransportAddress ta1 = TransportAddressFactory.CreateInstance("brunet.udp://10.5.144.69:5000");
      Assert.AreEqual(ta1.ToString(), "brunet.udp://10.5.144.69:5000/", "Testing TA parsing");
      
      TransportAddress ta2 = TransportAddressFactory.CreateInstance("brunet.udp://10.5.144.69:5000"); 
      Assert.AreEqual(ta1, ta2, "Testing TA Equals");
      
      string ta_string = "brunet.tunnel://UBU72YLHU5C3SY7JMYMJRTKK4D5BGW22/FE4QWASN+FE4QWASM";
      TransportAddress ta = TransportAddressFactory.CreateInstance("brunet.tunnel://UBU72YLHU5C3SY7JMYMJRTKK4D5BGW22/FE4QWASN+FE4QWASM");
      Assert.AreEqual(ta.ToString(), ta_string, "testing tunnel TA parsing");
      //Console.WriteLine(ta);

      TunnelTransportAddress tun_ta = (TunnelTransportAddress) TransportAddressFactory.CreateInstance("brunet.tunnel://OIHZCNNUAXTLLARQIOBNCUWXYNAS62LO/CADSL6GV+CADSL6GU");

      ArrayList fwd = new ArrayList();
      fwd.Add(new AHAddress(Base32.Decode("CADSL6GVVBM6V442CETP4JTEAWACLC5A")));
      fwd.Add(new AHAddress(Base32.Decode("CADSL6GUVBM6V442CETP4JTEAWACLC5A")));
      
      TunnelTransportAddress test_ta = new TunnelTransportAddress(tun_ta.Target, fwd);
      Assert.AreEqual(tun_ta, test_ta, "testing tunnel TA compression enhancements");
      //Console.WriteLine(tun_ta.ToString());
      //Console.WriteLine(test_ta.ToString());
      Assert.AreEqual(tun_ta.ToString(), test_ta.ToString(), "testing tunnel TA compression enhancements (toString)");

      Assert.AreEqual(tun_ta.ContainsForwarder(new AHAddress(Base32.Decode("CADSL6GVVBM6V442CETP4JTEAWACLC5A"))), true, 
		      "testing tunnel TA contains forwarder (1)");

      Assert.AreEqual(tun_ta.ContainsForwarder(new AHAddress(Base32.Decode("CADSL6GUVBM6V442CETP4JTEAWACLC5A"))), true, 
		      "testing tunnel TA contains forwarder (2)");

      
      
      string StrLocalHost = Dns.GetHostName();
      IPHostEntry IPEntry = Dns.GetHostByName(StrLocalHost);
      TransportAddress local_ta = TransportAddressFactory.CreateInstance("brunet.udp://" +  IPEntry.AddressList[0].ToString() + 
									 ":" + 5000);
      IEnumerable locals = TransportAddressFactory.CreateForLocalHost(TransportAddress.TAType.Udp, 5000);

      bool match = false;
      foreach (TransportAddress test_ta1 in locals) {
	//Console.WriteLine("test_ta: {0}", test_ta1);
	if (test_ta1.Equals(local_ta)) {
	  match = true;
	}
      }
      Assert.AreEqual(match, true, "testing local TA matches");
      //testing function TA
      TransportAddress func_ta = TransportAddressFactory.CreateInstance("brunet.function://localhost:3000");
      Assert.AreEqual(func_ta.ToString(), "brunet.function://localhost:3000/", "Testing function TA parsing");
      
    }
  }
#endif
}