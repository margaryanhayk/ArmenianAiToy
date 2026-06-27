import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { clearToken, getToken, saveToken } from './src/auth';
import LoginScreen from './src/screens/LoginScreen';
import DevicesScreen from './src/screens/DevicesScreen';

export default function App() {
  const [booting, setBooting] = useState(true);
  const [signedIn, setSignedIn] = useState(false);

  // Restore an existing session on launch.
  useEffect(() => {
    (async () => {
      const token = await getToken();
      setSignedIn(!!token);
      setBooting(false);
    })();
  }, []);

  async function handleLoggedIn(token: string) {
    await saveToken(token);
    setSignedIn(true);
  }

  async function handleLogout() {
    await clearToken();
    setSignedIn(false);
  }

  if (booting) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
        <StatusBar style="auto" />
      </View>
    );
  }

  return (
    <>
      {signedIn ? (
        <DevicesScreen onLogout={handleLogout} />
      ) : (
        <LoginScreen onLoggedIn={handleLoggedIn} />
      )}
      <StatusBar style="auto" />
    </>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
});
